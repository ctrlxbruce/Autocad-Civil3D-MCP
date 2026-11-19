using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Civil3DMcp
{
    /// <summary>
    /// Who may talk to the plugin, and what doc.run_command / doc.set_sysvar refuse.
    ///
    /// Each time the listener starts it makes a new random token and writes it to
    /// %LOCALAPPDATA%\Civil3DMcp\token-&lt;port&gt;, a folder only this Windows user can read. Every request must
    /// carry that token. A web page can send text to 127.0.0.1 but cannot read the file, so it can't drive
    /// Civil 3D; and the first line that isn't a JSON request (for example an HTTP header) closes the connection.
    /// </summary>
    public static class Security
    {
        public static string Token { get; private set; } = "";

        public static string TokenFile(int port) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Civil3DMcp", $"token-{port}");

        public static void NewToken(int port)
        {
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var file = TokenFile(port);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, Token, Encoding.ASCII);
        }

        public static void DropToken(int port)
        {
            Token = "";
            try { File.Delete(TokenFile(port)); } catch { }
        }

        public static bool TokenMatches(string given) =>
            Token.Length > 0 && given != null &&
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(Token));

        // Commands that start programs or load code (most are external commands from acad.pgp), lower Civil 3D's
        // own code-loading security, or close Civil 3D.
        static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "SHELL", "SH", "START", "EXPLORER", "NOTEPAD", "PBRUSH", "CATALOG", "DEL", "DIR", "EDIT", "TYPE", "BROWSER",
            "NETLOAD", "APPLOAD", "ARX", "SCRIPT", "VBALOAD", "VBARUN", "VBASTMT", "VBAIDE", "VBAMAN", "QUIT", "EXIT",
        };

        // System variables that control which code Civil 3D trusts and loads.
        static readonly HashSet<string> BlockedSysvars = new(StringComparer.OrdinalIgnoreCase)
        {
            "SECURELOAD", "TRUSTEDPATHS", "TRUSTEDDOMAINS", "LEGACYCODESEARCH", "ACADLSPASDOC",
        };

        public static bool IsBlockedSysvar(string name) => BlockedSysvars.Contains(name?.Trim() ?? "");

        public static IEnumerable<string> BlockedSysvarNames => BlockedSysvars;

        /// <summary>Throws when command-line text would run AutoLISP, start a program, load code or quit.</summary>
        public static void CheckCommand(string text)
        {
            if (text.Contains('(') && !AllowLisp)
                throw new InvalidOperationException(
                    "Blocked for safety: doc_run_command does not run AutoLISP. Use a dedicated tool or code_execute (C#) instead. "
                    + "(To allow it, set the Windows environment variable CIVIL3D_MCP_ALLOW_LISP=1 and restart Civil 3D.)");
            foreach (var raw in text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var word = raw.TrimStart('_', '.', '-', '+', '\'');
                if (BlockedCommands.Contains(word))
                    throw new InvalidOperationException(
                        $"Blocked for safety: doc_run_command does not run '{word.ToUpperInvariant()}' (it can start programs, load code or close Civil 3D). "
                        + "If the word is a value, use the dedicated tool instead.");
                if (BlockedSysvars.Contains(word))
                    throw new InvalidOperationException(
                        $"Blocked for safety: '{word.ToUpperInvariant()}' controls which code Civil 3D trusts; change it yourself in Civil 3D (OPTIONS).");
            }
        }

        static bool AllowLisp => Environment.GetEnvironmentVariable("CIVIL3D_MCP_ALLOW_LISP") == "1";
    }
}
