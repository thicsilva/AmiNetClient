using System.Security.Cryptography;
using System.Text;

namespace AnAmiClient;

public sealed partial class AmiNetClient
{
    public async Task<bool> Login(string username, string secret, bool md5 = true)
    {
        ArgumentNullException.ThrowIfNull(username);

        ArgumentNullException.ThrowIfNull(secret);

        AmiNetMessage request, response;

        if (md5)
        {
            request = new AmiNetMessage
            {
                { "Action", "Challenge" },
                { "AuthType", "MD5" },
            };

            response = await Publish(request);

            if (!(response["Response"] ?? string.Empty).Equals("Success", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            byte[] answer = MD5.HashData(Encoding.ASCII.GetBytes(response["Challenge"] + secret));

            string key = answer.Aggregate("", (current, t) => current + t.ToString("x2"));

            request = new AmiNetMessage
            {
                { "Action", "Login" },
                { "AuthType", "MD5" },
                { "Username", username },
                { "Key", key },
            };

            response = await Publish(request);
        }
        else
        {
            request = new AmiNetMessage
            {
                { "Action", "Login" },
                { "Username", username },
                { "Secret", secret },
            };

            response = await Publish(request);
        }

        return (response["Response"] ?? string.Empty).Equals("Success", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> Logoff()
    {
        AmiNetMessage request = new()
        {
            { "Action", "Logoff" },
        };

        AmiNetMessage response = await Publish(request);

        return (response["Response"] ?? string.Empty).Equals("Goodbye", StringComparison.OrdinalIgnoreCase);
    }
}