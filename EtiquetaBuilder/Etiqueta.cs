using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    // Clase base para elementos de la etiqueta
    public abstract class ElementoEtiqueta
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Rotacion { get; set; } // Nueva propiedad para rotación

        protected ElementoEtiqueta(float x, float y, float rotacion = 0)
        {
            X = x;
            Y = y;
            Rotacion = rotacion;
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
        public string FontFamilyName => Font.Typeface.FamilyName; // Exponer la fuente

        private float? _cachedAncho;
        private float? _cachedAltura;

        public ElementoTexto(string texto, float x, float y, SKTypeface typeface, float textSize, SKColor color, float rotacion = 0)
            : base(x, y, rotacion)
        {
            Texto = texto ?? string.Empty;
            Font = new SKFont(typeface ?? SKTypeface.FromFamilyName("Arial"), textSize);
            Paint = new SKPaint { Color = color, IsAntialias = true };
        }

        public override void Dibujar(SKCanvas canvas, object contexto)
        {
            canvas.Save();
            canvas.RotateDegrees(Rotacion, X, Y);
            canvas.DrawText(Texto, X, Y + Font.Size, SKTextAlign.Left, Font, Paint);
            canvas.Restore();
        }

        public override void Escalar(float factor)
        {
            X *= factor;
            Y *= factor;
            Font.Size *= factor;
            _cachedAncho = null; // Invalida el caché
            _cachedAltura = null;
        }

        public override float ObtenerAltura()
        {
            if (_cachedAltura == null)
            {
                _cachedAltura = Font.Size;
            }
            return _cachedAltura.Value;
        }

        public override float ObtenerAncho(SKCanvas canvas)
        {
            if (_cachedAncho == null)
            {
                // Usamos el servicio de medición centralizado
                _cachedAncho = SKUtil.MedirTexto(Texto, Font.Typeface, Font.Size);
            }
            return _cachedAncho.Value;
        }
    }

    public class ElementoImagen : ElementoEtiqueta
    {
        public SKBitmap Imagen { get; set; }
        public SKRect Rectangulo { get; set; }

        public ElementoImagen(SKBitmap imagen, float x, float y, float ancho, float alto, float rotacion = 0)
            : base(x, y, rotacion)
        {
            Imagen = imagen ?? throw new ArgumentNullException(nameof(imagen));
            Rectangulo = SKRect.Create(x, y, ancho, alto);
        }

        public override void Dibujar(SKCanvas canvas, object contexto)
        {
            canvas.Save();
            canvas.RotateDegrees(Rotacion, X, Y);
            canvas.DrawBitmap(Imagen, Rectangulo);
            canvas.Restore();
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

    public class ElementoCodigoBarras : ElementoEtiqueta
    {
        public string Codigo { get; set; }
        public SKRect Rectangulo { get; set; }

        public ElementoCodigoBarras(string codigo, float x, float y, int ancho, int alto, float rotacion = 0)
            : base(x, y, rotacion)
        {
            Codigo = codigo ?? string.Empty;
            Rectangulo = SKRect.Create(x, y, ancho, alto);
        }

        public override void Dibujar(SKCanvas canvas, object contexto)
        {
            var writer = new ZXing.BarcodeWriter<SKBitmap>
            {
                Format = ZXing.BarcodeFormat.CODE_128,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = (int)Rectangulo.Width,
                    Height = (int)Rectangulo.Height
                }
            };
            using var bitmap = writer.Write(Codigo);
            canvas.Save();
            canvas.RotateDegrees(Rotacion, X, Y);
            canvas.DrawBitmap(bitmap, Rectangulo);
            canvas.Restore();
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

    // La clase principal que representa la etiqueta
    public class Etiqueta
    {
        private readonly List<ElementoEtiqueta> _elementos = new List<ElementoEtiqueta>();
        public IReadOnlyList<ElementoEtiqueta> Elementos => _elementos.AsReadOnly();

        public float Ancho { get; private set; }
        public float Alto { get; private set; }

        public Etiqueta(float ancho, float alto)
        {
            if (ancho <= 0 || alto <= 0)
                throw new ArgumentException("Las dimensiones de la etiqueta deben ser mayores que 0.");
            Ancho = ancho;
            Alto = alto;
        }

        public void AgregarElemento(ElementoEtiqueta elemento)
        {
            _elementos.Add(elemento);
        }

        public void Escalar(float factor)
        {
            if (factor <= 0)
                throw new ArgumentException("El factor de escala debe ser mayor que 0.");
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

        // Serialización a JSON
        public string ToJson()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            return JsonSerializer.Serialize(this, options);
        }

        // Deserialización desde JSON
        public static Etiqueta FromJson(string json)
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            };
            return JsonSerializer.Deserialize<Etiqueta>(json, options);
        }
    }

    // Clase de utilidad para centralizar la medición de texto y evitar instanciar SKPaint cada vez
    // Clase de utilidad actualizada para medir texto usando SKFont.MeasureText
    public static class SKUtil
    {
        public static float MedirTexto(string texto, SKTypeface typeface, float textSize)
        {
            if (texto == null)
                throw new ArgumentNullException(nameof(texto));
            // Se crea un SKFont con la tipografía y tamaño indicados
            using var font = new SKFont(typeface, textSize);
            // Se mide el ancho del texto utilizando SKFont.MeasureText()
            return font.MeasureText(texto);
        }
    }


    // Builder para construir la etiqueta de forma fluida
    public class EtiquetaBuilder
    {
        private readonly Etiqueta _etiqueta;
        private object? _contexto;
        private float _ultimaY = 0;

        public EtiquetaBuilder(float ancho, float alto)
        {
            _etiqueta = new Etiqueta(ancho, alto);
        }

        public EtiquetaBuilder ConContexto<T>(T contexto) where T : class
        {
            _contexto = contexto;
            return this;
        }

        // Validación de parámetros y límites incluidos
        public EtiquetaBuilder AgregarTexto(string texto, float x, float y, SKTypeface typeface, float textSize, SKColor color,
            AlineacionHorizontal alineacion = AlineacionHorizontal.Izquierda, float rotacion = 0)
        {
            ValidarParametros(x, y, textSize);
            var elemento = new ElementoTexto(texto, x, y, typeface, textSize, color, rotacion);
            AlinearElemento(elemento, alineacion);
            ValidarLimites(elemento);
            _etiqueta.AgregarElemento(elemento);
            _ultimaY = Math.Max(_ultimaY, y + elemento.ObtenerAltura());
            return this;
        }

        public EtiquetaBuilder AgregarCodigoBarras(string codigo, float x, float y, int ancho, int alto,
            AlineacionHorizontal alineacion = AlineacionHorizontal.Izquierda, float rotacion = 0)
        {
            ValidarParametros(x, y, ancho, alto);
            var elemento = new ElementoCodigoBarras(codigo, x, y, ancho, alto, rotacion);
            AlinearElemento(elemento, alineacion);
            ValidarLimites(elemento);
            _etiqueta.AgregarElemento(elemento);
            _ultimaY = Math.Max(_ultimaY, y + elemento.ObtenerAltura());
            return this;
        }

        public EtiquetaBuilder AgregarImagen(SKBitmap imagen, float x, float y, float ancho, float alto,
            AlineacionHorizontal alineacion = AlineacionHorizontal.Izquierda, float rotacion = 0)
        {
            if (imagen == null)
                throw new ArgumentNullException(nameof(imagen));
            ValidarParametros(x, y, ancho, alto);
            var elemento = new ElementoImagen(imagen, x, y, ancho, alto, rotacion);
            AlinearElemento(elemento, alineacion);
            ValidarLimites(elemento);
            _etiqueta.AgregarElemento(elemento);
            _ultimaY = Math.Max(_ultimaY, y + elemento.ObtenerAltura());
            return this;
        }

        public EtiquetaBuilder AgregarTextoDividido(string texto, float x, float y, SKTypeface typeface, float textSize,
            int longitudMaxima, float espaciadoVertical, SKColor color, AlineacionHorizontal alineacion = AlineacionHorizontal.Izquierda, float rotacion = 0)
        {
            if (longitudMaxima <= 0)
                throw new ArgumentException("La longitud máxima debe ser mayor que 0.");
            texto ??= string.Empty;
            var lineas = DividirTexto(texto, longitudMaxima);
            foreach (var (linea, i) in lineas.Select((l, idx) => (l, idx)))
            {
                float yPos = y + (i * espaciadoVertical);
                var elemento = new ElementoTexto(linea, x, yPos, typeface, textSize, color, rotacion);
                AlinearElemento(elemento, alineacion);
                ValidarLimites(elemento);
                _etiqueta.AgregarElemento(elemento);
                _ultimaY = Math.Max(_ultimaY, elemento.Y + elemento.ObtenerAltura());
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

        // Centralizamos la alineación usando la medición centralizada
        private void AlinearElemento(ElementoEtiqueta elemento, AlineacionHorizontal alineacion)
        {
            // Se reutiliza un canvas mínimo para obtener la medida
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

        // Validación de límites para que los elementos no se salgan de la etiqueta
        private void ValidarLimites(ElementoEtiqueta elemento)
        {
            using var bitmap = new SKBitmap(1, 1);
            using var canvas = new SKCanvas(bitmap);
            float anchoElemento = elemento.ObtenerAncho(canvas);
            float alturaElemento = elemento.ObtenerAltura();

            if (elemento.X < 0) elemento.X = 0;
            if (elemento.Y < 0) elemento.Y = 0;
            if (elemento.X + anchoElemento > _etiqueta.Ancho) elemento.X = _etiqueta.Ancho - anchoElemento;
            if (elemento.Y + alturaElemento > _etiqueta.Alto) elemento.Y = _etiqueta.Alto - alturaElemento;
        }

        // Validación básica de parámetros numéricos
        private void ValidarParametros(params float[] valores)
        {
            if (valores.Any(v => v < 0))
                throw new ArgumentException("Los valores de posición y dimensiones deben ser mayores o iguales a 0.");
        }

        public Etiqueta Construir() => _etiqueta;

        // Genera la etiqueta, invocando el renderAction con el SKBitmap resultante
        public EtiquetaBuilder Generar(Action<SKBitmap> renderAction)
        {
            _etiqueta.Generar(renderAction, _contexto);
            return this;
        }
    }

    // Nota: Para extender la librería, basta con crear nuevas clases que hereden de ElementoEtiqueta,
    // por ejemplo, ElementoForma, ElementoLinea, etc. y luego agregar métodos en el builder para gestionarlos.
}
