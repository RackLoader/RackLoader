using System;
using System.Text;

namespace RackLoader.Rkm;

public static class RkmPem
{
    public static string EncodeSubjectPublicKeyInfo(byte[] subjectPublicKeyInfo)
    {
        return Encode("PUBLIC KEY", subjectPublicKeyInfo);
    }

    public static string EncodeEncryptedPrivateKeyInfo(byte[] encryptedPrivateKeyInfo)
    {
        return Encode("ENCRYPTED PRIVATE KEY", encryptedPrivateKeyInfo);
    }

    private static string Encode(string label, byte[] contents)
    {
        var base64 = Convert.ToBase64String(contents);
        var output = new StringBuilder($"-----BEGIN {label}-----\n");
        for (var offset = 0; offset < base64.Length; offset += 64)
            output.AppendLine(base64.Substring(offset, Math.Min(64, base64.Length - offset)));
        output.Append($"-----END {label}-----\n");
        return output.ToString();
    }
}
