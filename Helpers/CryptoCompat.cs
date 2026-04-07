using System;
using System.Security.Cryptography;
using System.Text;

namespace Ktuvit.Plugin.Helpers
{
    public static class CryptoCompat
    {
        public static string EncryptKtuvitPassword(string email, string password, string salt)
        {
            try
            {
                // 1. IV: mimic CryptoJS.enc.Hex.parse(email)
                byte[] ivRaw = ParseCryptoJsHex(email);
                byte[] iv = new byte[16];
                if (ivRaw.Length >= 16)
                    Array.Copy(ivRaw, iv, 16);
                else if (ivRaw.Length > 0)
                    Array.Copy(ivRaw, iv, ivRaw.Length);
                // remaining bytes are 0

                // 2. PBKDF2 derive key (AES-128, 3000 iterations, SHA1)
                var pbkdf2 = new Rfc2898DeriveBytes(
                    Encoding.UTF8.GetBytes(salt),
                    Encoding.UTF8.GetBytes(email),
                    3000
                );
                byte[] key = pbkdf2.GetBytes(16); // 128-bit key

                // 3. Encrypt password (AES-CBC, PKCS7)
                using var aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;

                using var encryptor = aes.CreateEncryptor();
                byte[] plainBytes = Encoding.UTF8.GetBytes(password);
                byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                // 4. Final hash: Base64(SHA256(cipherBytes))
                using var sha256 = SHA256.Create();
                byte[] hashBytes = sha256.ComputeHash(cipherBytes);
                string encPass = Convert.ToBase64String(hashBytes);

                return encPass;
            }
            catch
            {
                return string.Empty;
            }
        }
        private static byte[] ParseCryptoJsHex(string input)
        {
            var bytes = new System.Collections.Generic.List<byte>();

            for (int i = 0; i < input.Length; i += 2)
            {
                string pair = (i + 2 <= input.Length)
                    ? input.Substring(i, 2)
                    : input.Substring(i, 1);

                char c1 = pair[0];
                bool c1Hex = Uri.IsHexDigit(c1);
                bool c2Hex = (pair.Length > 1 && Uri.IsHexDigit(pair[1]));

                int value;
                if (c1Hex && c2Hex)
                    value = Convert.ToInt32(pair, 16);
                else if (c1Hex)
                    value = Convert.ToInt32(pair.Substring(0, 1), 16);
                else
                    value = 0; // CryptoJS.parseInt("xx",16) -> NaN -> 0

                bytes.Add((byte)value);
            }

            return bytes.ToArray();
        }
    }
}
