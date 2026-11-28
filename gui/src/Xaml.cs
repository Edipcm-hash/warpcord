using System;
using System.IO;
using System.Text;
using System.Windows.Markup;

namespace WarpCord
{
    /// <summary>Loads embedded XAML from the assembly's manifest resources.</summary>
    internal static class Xaml
    {
        public static T Load<T>(string resourceName) where T : class
        {
            var asm = typeof(Xaml).Assembly;
            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    var found = new StringBuilder();
                    foreach (var name in asm.GetManifestResourceNames()) found.Append(name).Append(' ');
                    throw new InvalidOperationException(
                        "XAML kaynagi bulunamadi: " + resourceName + " (var olan: " + found + ")");
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    object parsed = XamlReader.Parse(reader.ReadToEnd());
                    var typed = parsed as T;
                    if (typed == null)
                    {
                        throw new InvalidCastException(
                            "XAML koku beklenen tipte degil (" + typeof(T).Name + "): " + resourceName);
                    }
                    return typed;
                }
            }
        }
    }
}
