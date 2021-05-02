using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace NeteaseCloudMusicApi.Utils {
	internal static class Crypto {
		private static readonly byte[] iv = Encoding.ASCII.GetBytes("0102030405060708");
		private static readonly byte[] presetKey = Encoding.ASCII.GetBytes("0CoJUm6Qyw8W8jud");
		private static readonly byte[] linuxapiKey = Encoding.ASCII.GetBytes("rFgB&h#%2?^eDg:Q");
		private const string base62 = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
		private static readonly RSAParameters publicKey = ParsePublicKey("-----BEGIN PUBLIC KEY-----\nMIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDgtQn2JZ34ZC28NWYpAUd98iZ37BUrX/aKzmFbt7clFSs6sXqHauqKWqdtLkF2KexO40H1YTX8z2lSgBBOAxLsvaklV8k4cBFK9snQXE9/DDaFt6Rr7iVZMldczhC0JNgTz+SHXT6CBHuX3e9SdB1Ua44oncaTWz7OBGLbCiK45wIDAQAB\n-----END PUBLIC KEY-----");
		private static readonly byte[] eapiKey = Encoding.ASCII.GetBytes("e82ckenh8dichen8");

		public static Dictionary<string, string> WEApi(object @object) {
			string text = JsonConvert.SerializeObject(@object);
			byte[] secretKey = new Random().RandomBytes(16);
			secretKey = secretKey.Select(n => (byte)base62[n % 62]).ToArray();
			return new Dictionary<string, string> {
				["params"] = AesEncrypt(AesEncrypt(text.ToByteArrayUtf8(), CipherMode.CBC, presetKey, iv).ToBase64String().ToByteArrayUtf8(), CipherMode.CBC, secretKey, iv).ToBase64String(),
				["encSecKey"] = RsaEncrypt(secretKey.Reverse().ToArray(), publicKey).ToHexStringLower()
			};
		}

		public static Dictionary<string, string> LinuxApi(object @object) {
			string text = JsonConvert.SerializeObject(@object);
			return new Dictionary<string, string> {
				["eparams"] = AesEncrypt(text.ToByteArrayUtf8(), CipherMode.ECB, linuxapiKey, null).ToHexStringUpper()
			};
		}

		public static Dictionary<string, string> EApi(string url, object @object) {
			string text = JsonConvert.SerializeObject(@object);
			string message = $"nobody{url}use{text}md5forencrypt";
			string digest = message.ToByteArrayUtf8().ComputeMd5().ToHexStringLower();
			string data = $"{url}-36cd479b6b5-{text}-36cd479b6b5-{digest}";
			return new Dictionary<string, string> {
				["params"] = AesEncrypt(data.ToByteArrayUtf8(), CipherMode.ECB, eapiKey, null).ToHexStringUpper()
			};
		}

		public static byte[] Decrypt(byte[] cipherBuffer) {
			return AesDecrypt(cipherBuffer, CipherMode.ECB, eapiKey, null);
		}

		private static byte[] AesEncrypt(byte[] buffer, CipherMode mode, byte[] key, byte[] iv) {
			 var aes = Aes.Create();
			aes.BlockSize = 128;
			aes.Key = key;
			if (!(iv is null))
				aes.IV = iv;
			aes.Mode = mode;
			 var cryptoTransform = aes.CreateEncryptor();
			return cryptoTransform.TransformFinalBlock(buffer, 0, buffer.Length);
		}

		private static byte[] AesDecrypt(byte[] buffer, CipherMode mode, byte[] key, byte[] iv) {
			 var aes = Aes.Create();
			aes.BlockSize = 128;
			aes.Key = key;
			if (!(iv is null))
				aes.IV = iv;
			aes.Mode = mode;
			 var cryptoTransform = aes.CreateDecryptor();
			return cryptoTransform.TransformFinalBlock(buffer, 0, buffer.Length);
		}

		private static byte[] RsaEncrypt(byte[] buffer, RSAParameters key) {
			return GetByteArrayBigEndian(BigInteger.ModPow(GetBigIntegerBigEndian(buffer), GetBigIntegerBigEndian(key.Exponent), GetBigIntegerBigEndian(key.Modulus)));
		}

        private static byte[] GetByteArrayBigEndian(BigInteger value)
        {
            byte[] array = value.ToByteArray();
            if (array[array.Length - 1] == 0)
            {
                byte[] array2 = new byte[array.Length - 1];
                Buffer.BlockCopy(array, 0, array2, 0, array2.Length);
                array = array2;
            }
            for (int i = 0; i < array.Length / 2; i++)
            {
                byte t = array[i];
                array[i] = array[array.Length - i - 1];
                array[array.Length - i - 1] = t;
            }
            return array;
        }

        private static BigInteger GetBigIntegerBigEndian(byte[] value)
        {
            byte[] value2 = new byte[value.Length + 1];
            for (int i = 0; i < value.Length; i++)
                value2[value2.Length - i - 2] = value[i];
            return new BigInteger(value2);
        }

        private static RSAParameters ParsePublicKey(string publicKey) {
			publicKey = publicKey.Replace("\n", string.Empty);
			publicKey = publicKey.Substring(26, publicKey.Length - 50);
			 var stream = new MemoryStream(Convert.FromBase64String(publicKey));
			 var reader = new BinaryReader(stream);

			ushort i16 = reader.ReadUInt16();
			if (i16 == 0x8130)
				reader.ReadByte();
			else if (i16 == 0x8230)
				reader.ReadInt16();
			else
				throw new ArgumentException(nameof(publicKey));

			byte[] oid = reader.ReadBytes(15);
			if (!oid.SequenceEqual(new byte[] { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00 }))
				throw new ArgumentException(nameof(publicKey));

			i16 = reader.ReadUInt16();
			if (i16 == 0x8103)
				reader.ReadByte();
			else if (i16 == 0x8203)
				reader.ReadInt16();
			else
				throw new ArgumentException(nameof(publicKey));

			byte i8 = reader.ReadByte();
			if (i8 != 0x00)
				throw new ArgumentException(nameof(publicKey));
			i16 = reader.ReadUInt16();
			if (i16 == 0x8130)
				reader.ReadByte();
			else if (i16 == 0x8230)
				reader.ReadInt16();
			else
				throw new ArgumentException(nameof(publicKey));

			i16 = reader.ReadUInt16();
			byte high;
			byte low;
			if (i16 == 0x8102) {
				high = 0;
				low = reader.ReadByte();
			}
			else if (i16 == 0x8202) {
				high = reader.ReadByte();
				low = reader.ReadByte();
			}
			else
				throw new ArgumentException(nameof(publicKey));

			int modulusLength = BitConverter.ToInt32(new byte[] { low, high, 0x00, 0x00 }, 0);
			if (reader.PeekChar() == 0x00) {
				reader.ReadByte();
				modulusLength -= 1;
			}

			byte[] modulus = reader.ReadBytes(modulusLength);
			if (reader.ReadByte() != 0x02)
				throw new ArgumentException(nameof(publicKey));

			int exponentLength = reader.ReadByte();
			byte[] exponent = reader.ReadBytes(exponentLength);

			return new RSAParameters {
				Modulus = modulus,
				Exponent = exponent
			};
		}
	}
}
