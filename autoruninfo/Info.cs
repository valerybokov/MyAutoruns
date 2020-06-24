using System;

namespace AutorunInfo
{
    /// <summary>
    /// Информация об объекте
    /// </summary>
    public class Info
    {
        public Info(string runtype)
        {
            RunType = runtype;
        }

        public System.Windows.Media.ImageSource Icon { get; set; }

        public string Directory { get; set; }

        public string Manufacturer { get; set; }

        public bool IsSignatureExists { get; set; }

        public bool IsSignatureValid { get; set; }

        public string CmdArguments { get; set; }

        public string FileName { get; set; }

        public string RunType { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is Info)
            {
                var info = (Info)obj;

                return
                    Manufacturer == info.Manufacturer && Directory == info.Directory
                    && FileName == info.FileName && CmdArguments == info.CmdArguments;
            }
            else
                return false;
        }

        public override int GetHashCode()
        {
            var hashCode = 304851041;

            if (Manufacturer == null)
                hashCode = hashCode * -1521134295;
            else
                hashCode = hashCode * -1521134295 + Manufacturer.GetHashCode();

            if (CmdArguments == null)
                hashCode = hashCode * -1521134295;
            else
                hashCode = hashCode * -1521134295 + CmdArguments.GetHashCode();

            return hashCode * -1521134295 + FileName.GetHashCode();
        }
    }
}
