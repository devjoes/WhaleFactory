using System.IO;

namespace WhaleFactory
{
    public class FileInfoKey
    {
        public FileInfoKey(string path)
        {
            this.Info = new FileInfo(path);
        }
        public FileInfoKey(FileInfo fi)
        {
            this.Info = fi;
        }

        public FileInfo Info { get; }

        public override int GetHashCode()
        {
            string path = this.Info.FullName;
            return path.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            if (obj is FileInfoKey fik)
            {
                return this.Info.FullName.Equals(fik.Info.FullName);
            }

            return false;
        }
    }
}