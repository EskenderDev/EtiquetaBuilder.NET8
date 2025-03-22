using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Etiqueta
{
    public enum AlineacionHorizontal
    {
        Izquierda,
        Centro,
        Derecha
    }

    public interface ICondicion<T>
    {
        bool Evaluar(T contexto);
    }

    public class LambdaCondicion<T> : ICondicion<T>
    {
        private readonly Func<T, bool> _condicion;

        public LambdaCondicion(Func<T, bool> condicion) => _condicion = condicion;

        public bool Evaluar(T contexto) => _condicion(contexto);
    }

    public abstract class ElementoEtiqueta
    {
        public float X { get; set; }
        public float Y { get; set; }

        protected ElementoEtiqueta(float x, float y)
        {
            X = x;
            Y = y;
        }

        public abstract void Dibujar(SKCanvas canvas, object contexto);
        public abstract void Escalar(float factor);
        public abstract float ObtenerAltura();
        public abstract float ObtenerAncho(SKCanvas canvas);
    }

    public class ElementoTexto : ElementoEtiqueta
    {
        public string Texto { get; set; }
        public SKPaint Paint { get; set; }
        public SKFont Font { get; set; }

        public ElementoTexto(string texto, float x, float y, SKTypeface typeface, float textSize, SKColor color)
            : base(x, y)
        {
            Texto = texto ?? string.Empty;
            Font = new SKFont(typeface ?? SKTypeface.FromFamilyName("Arial"), textSize);
            Paint = new SKPaint
            {
                Color = color,
                IsAntialias = true
            };
        }

        public override void Dibujar(SKCanvas canvas, object contexto)
        {
            canvas.DrawText(Texto, X, Y + Font.Size, SKTextAlign.Left, Font, Paint); // Ajuste Y para alinear correctamente
        }

        public override void Escalar(float factor)
        {
            X *= factor;
            Y *= factor;
            Font.Size *= factor;
        }

        public override float ObtenerAltura() => Font.Size;

        public override float ObtenerAncho(SKCanvas canvas)
        {
            using var paint = new SKPaint();
            paint.Typeface = Font.Typeface;
            return paint.MeasureText(Texto);
        }
    }

    // ... (resto del código)

    public class ElementoCodigoBarras : ElementoEtiqueta
    {
        public string Codigo { get; set; }
        public SKRect Rectangulo { get; set; }

        public ElementoCodigoBarras(string codigo, float x, float y, int ancho, int alto)
            : base(x, y)
        {
            Codigo = codigo ?? string.Empty;
            Rectangulo = SKRect.Create(x, y, ancho, alto);
        }

        public override void Dibujar(SKCanvas canvas, object contexto)
        {
            var writer = new BarcodeWriter<SKBitmap>
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = (int)Rectangulo.Width,
                    Height = (int)Rectangulo.Height
                }
            };
            using var bitmap = writer.Write(Codigo);
            canvas.DrawBitmap(bitmap, Rectangulo);
        }

        public override void Escalar(float factor)
        {
            X *= factor;
            Y *= factor;
            Rectangulo = SKRect.Create(
                Rectangulo.Left * factor,
                Rectangulo.Top * factor,
                Rectangulo.Width * factor,
                Rectangulo.Height * factor);
        }

        public override float ObtenerAltura() => Rectangulo.Height;

        public override float ObtenerAncho(SKCanvas canvas) => Rectangulo.Width;
    }

    public class ElementoCondicional<T> : ElementoEtiqueta
    {
        public ElementoEtiqueta Elemento { get; }
        public ICondicion<T> Condicion { get; }

        public ElementoCondicional(ElementoEtiqueta elemento, ICondicion<T> condicion)
            : base(elemento.X, elemento.Y)
        {
            Elemento = elemento ?? throw new ArgumentNullException(nameof(elemento));
            Condicion = condicion ?? throw new ArgumentNullException(nameof(condicion));
        }

        public override void Dibujar(SKCanvas canvas, object contexto)
        {
            if (contexto is T typedContext && Condicion.Evaluar(typedContext))
            {
                Elemento.Dibujar(canvas, contexto);
            }
        }

        public override void Escalar(float factor)
        {
            X *= factor;
            Y *= factor;
            Elemento.Escalar(factor);
        }

        public override float ObtenerAltura() => Elemento.ObtenerAltura();

        public override float ObtenerAncho(SKCanvas canvas) => Elemento.ObtenerAncho(canvas);
    }

    public class Etiqueta
    {
        internal readonly List<ElementoEtiqueta> _elementos = [];
        public float Ancho { get; private set; }
        public float Alto { get; private set; }

        public Etiqueta(float ancho, float alto)
        {
            Ancho = ancho;
            Alto = alto;
        }

        public void AgregarElemento(ElementoEtiqueta elemento) => _elementos.Add(elemento);

        public void Escalar(float factor)
        {
            Ancho *= factor;
            Alto *= factor;
            foreach (var elemento in _elementos)
            {
                elemento.Escalar(factor);
            }
        }

        public void Generar(Action<SKBitmap> renderAction, object contexto)
        {
            using var bitmap = new SKBitmap((int)Ancho, (int)Alto);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);

            foreach (var elemento in _elementos)
            {
                elemento.Dibujar(canvas, contexto);
            }

            renderAction(bitmap);
        }
    }

    public class EtiquetaBuilder(float ancho, float alto)
    {
        private readonly Etiqueta _etiqueta = new Etiqueta(ancho, alto);
        private object? _contexto;
        private float _ultimaY = 0;
        private bool _condicionEjecutada = false;

        public EtiquetaBuilder ConContexto<T>(T contexto) where T : class
        {
            _contexto = contexto;
            return this;
        }

        public EtiquetaBuilder AgregarTexto(string texto, float x, float y, SKTypeface typeface, float textSize, SKColor color, AlineacionHorizontal alineacion = AlineacionHorizontal.Izquierda)
        {
            var elemento = new ElementoTexto(texto, x, y, typeface, textSize, color);
            AlinearElemento(elemento, alineacion);
            _etiqueta.AgregarElemento(elemento);
            _ultimaY = Math.Max(_ultimaY, y + elemento.ObtenerAltura());
            return this;
        }

        public EtiquetaBuilder AgregarCodigoBarras(string codigo, float x, float y, int ancho, int alto, AlineacionHorizontal alineacion = AlineacionHorizontal.Izquierda)
        {
            var elemento = new ElementoCodigoBarras(codigo, x, y, ancho, alto);
            AlinearElemento(elemento, alineacion);
            _etiqueta.AgregarElemento(elemento);
            _ultimaY = Math.Max(_ultimaY, y + elemento.ObtenerAltura());
            return this;
        }

        public EtiquetaBuilder AgregarTextoDividido(string texto, float x, float y, SKTypeface typeface, float textSize, int longitudMaxima, float espaciadoVertical, SKColor color, AlineacionHorizontal alineacion = AlineacionHorizontal.Izquierda)
        {
            texto ??= string.Empty;
            ArgumentNullException.ThrowIfNull(typeface);

            var lineas = DividirTexto(texto, longitudMaxima);
            foreach (var (linea, i) in lineas.Select((l, idx) => (l, idx)))
            {
                float yPos = y + (i * espaciadoVertical);
                if (yPos < 0) yPos = 0;

                var elemento = new ElementoTexto(linea, x, yPos, typeface, textSize, color);
                AlinearElemento(elemento, alineacion);

                using var bitmap = new SKBitmap(1, 1);
                using var canvas = new SKCanvas(bitmap);
                float anchoElemento = elemento.ObtenerAncho(canvas);
                if (elemento.X < 0) elemento.X = 0;
                if (elemento.X + anchoElemento > _etiqueta.Ancho) elemento.X = _etiqueta.Ancho - anchoElemento;

                _etiqueta.AgregarElemento(elemento);
                _ultimaY = Math.Max(_ultimaY, elemento.Y + elemento.ObtenerAltura());

#if DEBUG
                Console.WriteLine($"Texto: '{linea}', X: {elemento.X}, Y: {elemento.Y}, Altura: {elemento.ObtenerAltura()}");
#endif
            }
            return this;
        }

        private static List<string> DividirTexto(string texto, int longitudMaxima)
        {
            var lineas = new List<string>();
            if (string.IsNullOrEmpty(texto))
            {
                lineas.Add(string.Empty);
                return lineas;
            }

            while (texto.Length > longitudMaxima)
            {
                lineas.Add(texto[..longitudMaxima]);
                texto = texto[longitudMaxima..];
            }
            if (!string.IsNullOrEmpty(texto))
            {
                lineas.Add(texto);
            }
            return lineas;
        }

        private void AlinearElemento(ElementoEtiqueta elemento, AlineacionHorizontal alineacion)
        {
            using var bitmap = new SKBitmap(1, 1);
            using var canvas = new SKCanvas(bitmap);
            float anchoElemento = elemento.ObtenerAncho(canvas);
            elemento.X = alineacion switch
            {
                AlineacionHorizontal.Izquierda => 5,
                AlineacionHorizontal.Centro => (_etiqueta.Ancho - anchoElemento) / 2f,
                AlineacionHorizontal.Derecha => _etiqueta.Ancho - anchoElemento - 5,
                _ => elemento.X
            };
        }

        public EtiquetaBuilder If<T>(Func<T, bool> condicion, Action<EtiquetaBuilder> configuracion) where T : class
        {
            _condicionEjecutada = false;
            if (_contexto is T ctx && !_condicionEjecutada && condicion(ctx))
            {
                configuracion(this);
                _condicionEjecutada = true;
            }
            return this;
        }

        public EtiquetaBuilder ElseIf<T>(Func<T, bool> condicion, Action<EtiquetaBuilder> configuracion) where T : class
        {
            if (_contexto is T ctx && !_condicionEjecutada && condicion(ctx))
            {
                configuracion(this);
                _condicionEjecutada = true;
            }
            return this;
        }

        public EtiquetaBuilder Else(Action<EtiquetaBuilder> configuracion)
        {
            if (!_condicionEjecutada)
            {
                configuracion(this);
                _condicionEjecutada = true;
            }
            return this;
        }

        public EtiquetaBuilder For(int inicio, int fin, Action<EtiquetaBuilder, int> configuracion)
        {
            for (int i = inicio; i < fin; i++)
            {
                configuracion(this, i);
            }
            return this;
        }

        public EtiquetaBuilder ForEach<T>(IEnumerable<T> items, Action<EtiquetaBuilder, T> configuracion)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var item in items)
            {
                configuracion(this, item);
            }
            return this;
        }

        public EtiquetaBuilder Escalar(float factor)
        {
            if (factor <= 0) throw new ArgumentException("El factor de escala debe ser mayor que 0.");
            _etiqueta.Escalar(factor);
            _ultimaY *= factor;
            return this;
        }

        public EtiquetaBuilder EscalarDinamicamente(float anchoObjetivo, float altoObjetivo)
        {
            if (anchoObjetivo <= 0 || altoObjetivo <= 0)
                throw new ArgumentException("Las dimensiones objetivo deben ser mayores que 0.");

            float factor = Math.Min(anchoObjetivo / _etiqueta.Ancho, altoObjetivo / _etiqueta.Alto);
            _etiqueta.Escalar(factor);
            _ultimaY *= factor;
            return this;
        }

        public float ObtenerUltimaY() => _ultimaY;

        public EtiquetaBuilder CentrarVerticalmente()
        {
            if (_etiqueta._elementos.Count == 0) return this;

            float alturaTotal = _ultimaY;
            float desplazamiento = (_etiqueta.Alto - alturaTotal) / 2f;

            foreach (var elemento in _etiqueta._elementos)
            {
                elemento.Y += desplazamiento;
            }
            _ultimaY += desplazamiento;
            return this;
        }

        public Etiqueta Construir() => _etiqueta;

        public EtiquetaBuilder Generar(Action<SKBitmap> renderAction)
        {
            _etiqueta.Generar(renderAction, _contexto);
            _condicionEjecutada = false;
            return this;
        }
    }
}