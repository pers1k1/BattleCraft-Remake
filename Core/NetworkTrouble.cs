using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace CustomLauncher.Core
{
    public static class NetworkTrouble
    {
        // WHY: обрыв TLS приходит как IOException внутри HttpRequestException с текстом
        // WHY: про transport stream, и без разбора игрок видит только английскую строку
        private static readonly string[] Signatures =
        {
            "transport stream",
            "unexpected eof",
            "ssl",
            "tls",
            "secure channel",
            "connection was forcibly closed",
            "connection was aborted",
            "connection attempt failed",
            "remote name could not be resolved",
            "timed out",
            "no such host"
        };

        public static bool Looks(Exception? error)
        {
            for (Exception? current = error; current != null; current = current.InnerException)
            {
                if (current is HttpRequestException or SocketException or AuthenticationException) return true;

                if (current is IOException && Matches(current.Message)) return true;

                if (Matches(current.Message)) return true;
            }

            return false;
        }

        public static string Explain(Exception error) =>
            Lang.T("Связь с сервером оборвалась на середине запроса.")
            + "\n\n" + Lang.T("Так себя ведут: проверка HTTPS в антивирусе, VPN или прокси, блокировка со стороны провайдера и нестабильный Wi-Fi.")
            + "\n" + Lang.T("Что помогает: выключить проверку защищённых соединений в антивирусе, включить или наоборот выключить VPN, сменить сеть и повторить попытку.")
            + "\n\n" + Lang.F("Текст ошибки: {0}", Deepest(error).Message);

        public static Exception Deepest(Exception error)
        {
            Exception current = error;
            while (current.InnerException != null) current = current.InnerException;
            return current;
        }

        private static bool Matches(string message)
        {
            foreach (string signature in Signatures)
            {
                if (message.Contains(signature, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
