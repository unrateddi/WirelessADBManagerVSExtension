using System;
using System.Security.Cryptography;
using System.Text;

namespace WirelessADBManagerVSExtension.Utils;

internal static class KeyGenerator
{
    static readonly char[] s_chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();

    internal static string GetUniqueKey(int size)
    {
        byte[] data = new byte[4 * size];
        using (var crypto = RandomNumberGenerator.Create())
        {
            crypto.GetBytes(data);
        }
        StringBuilder result = new(size);
        for (int i = 0; i < size; i++)
        {
            var rnd = BitConverter.ToUInt32(data, i * 4);
            var idx = rnd % s_chars.Length;

            result.Append(s_chars[idx]);
        }

        return result.ToString();
    }
}
