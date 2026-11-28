using System;
using System.IO;

namespace WarpCord
{
    internal sealed class SignatureInfo
    {
        public bool Signed;
        public string Subject = "";
        public string Status = "Unknown";
    }

    /// <summary>
    /// Authenticode presence, read straight from the PE security directory (cheap
    /// and PowerShell-free), plus the signer subject when a signature exists.
    /// Tells the user whether Smart App Control will run this exe at all.
    /// </summary>
    internal static class Signature
    {
        public static SignatureInfo Read(string path)
        {
            var info = new SignatureInfo();
            if (string.IsNullOrEmpty(path))
            {
                try { path = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; }
                catch { return info; }
            }

            try
            {
                info.Signed = HasAuthenticode(path);
                info.Status = info.Signed ? "Signed" : "NotSigned";
            }
            catch
            {
                return info;
            }

            if (info.Signed)
            {
                try
                {
                    string subject = ScriptHost.RunCommand(
                        "(Get-AuthenticodeSignature -LiteralPath '" + path + "').SignerCertificate.Subject", 20000);
                    info.Subject = (subject ?? "").Trim();
                }
                catch
                {
                    // a subject is a nicety; the PE flag already answered the question
                }
            }
            return info;
        }

        /// <summary>True when the PE image carries an Authenticode certificate table.</summary>
        private static bool HasAuthenticode(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 0x40) return false;
                stream.Position = 0x3C;
                int peOffset = reader.ReadInt32();
                if (peOffset <= 0 || peOffset + 24 > stream.Length) return false;

                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550) return false;   // "PE\0\0"

                stream.Position = peOffset + 4 + 16;                   // COFF header: SizeOfOptionalHeader
                ushort optionalSize = reader.ReadUInt16();
                int optionalStart = peOffset + 24;
                if (optionalStart + optionalSize > stream.Length) return false;

                stream.Position = optionalStart;
                ushort magic = reader.ReadUInt16();
                bool pe32Plus = magic == 0x20b;
                if (!pe32Plus && magic != 0x10b) return false;

                int securityEntry = optionalStart + (pe32Plus ? 112 : 96) + 4 * 8;
                if (securityEntry + 8 > optionalStart + optionalSize) return false;

                stream.Position = securityEntry;
                int certificateOffset = reader.ReadInt32();
                int certificateSize = reader.ReadInt32();
                return certificateSize > 0 && certificateOffset > 0 &&
                       (long)certificateOffset + certificateSize <= stream.Length;
            }
        }
    }
}
