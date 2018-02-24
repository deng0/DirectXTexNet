using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DirectXTexNet.Test.Util
{
    static class BitmapExtensions
    {
        // Get a raw byte array from a bitmap, in RGBA format (*not* BGRA).
        public static byte[] GetRawBytesRGBA(this Bitmap bitmap)
        {
            var bits = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            var result = new byte[bitmap.Width * bitmap.Height * 4];
            var destIndex = 0;
            var byteWidth = bitmap.Width * 4;

            var src = bits.Scan0;

            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(src, result, destIndex, byteWidth);
                src += bits.Stride;
                destIndex += byteWidth;
            }

            ConvertBgraToRgba(result);
            bitmap.UnlockBits(bits);

            return result;
        }

        private static void ConvertBgraToRgba(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i += 4)
            {
                var temp = bytes[i];
                bytes[i] = bytes[i + 2];
                bytes[i + 2] = temp;
            }
        }
    }
}
