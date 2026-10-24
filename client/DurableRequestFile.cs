using System.IO;
using System.Text;

namespace RippersCasino.Client
{
    internal static class DurableRequestFile
    {
        internal static void WriteNew(string path, string json)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new System.ArgumentException("A recovery directory is required.", nameof(path));
            Directory.CreateDirectory(directory);
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            File.Move(path + ".tmp", path);
        }
    }
}
