using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CropPNG
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        static bool IsConsole;
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            bool overwrite = true;
            var args_list = (args ?? new string[0]).ToList();
            if (args_list.Count > 0)
            {
                for (int i = args_list.Count - 1; i >= 0; i--)
                {
                    if (args[i].Length != 2) continue;
                    switch (args_list[i])
                    {
                        case @"/n":
                            overwrite = false;
                            args_list.RemoveAt(i);
                            break;
                        default:
                            if (args[i].Length == 2)
                            {
                                Alert("Unknown argument " + args_list[i]);
                                return;
                            }
                            break;
                    }
                }
            }

            if (args_list.Count == 0)
            {
                IsConsole = GetConsoleWindow() != IntPtr.Zero;
                if (Debugger.IsAttached)
                {
                    OpenFileDialog ofd = new OpenFileDialog();
                    ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                    ofd.AutoUpgradeEnabled = true;
                    ofd.AddExtension = true;
                    ofd.RestoreDirectory = true;
                    if (ofd.ShowDialog() != DialogResult.OK)
                        return;
                    overwrite = MessageBox.Show("Overwrite the original?", "Overwrite", MessageBoxButtons.YesNo) == DialogResult.Yes;
                    args_list.Add(ofd.FileName);
                }
                else
                {
                    Alert("Drag a file to crop");
                    return;
                }
            }

            string filename = string.Join(" ", args_list);
            FileInfo finfo = new FileInfo(filename);
            if (!finfo.Exists)
            {
                Alert(filename + " does not exist");
                return;
            }
            if (finfo.Extension.ToUpperInvariant().Trim('.') != "PNG")
            {
                Alert(finfo.Extension + " not supported");
                return;
            }
            CropImage(filename, overwrite);
            File.SetLastWriteTimeUtc(filename, finfo.LastWriteTimeUtc.AddSeconds(1));
            File.SetCreationTimeUtc(filename, finfo.CreationTimeUtc.AddSeconds(1));
        }

        static void Alert(string message)
        {
            IsConsole = GetConsoleWindow() != IntPtr.Zero;
            if (IsConsole) { Console.WriteLine(message); }
            else { MessageBox.Show(message); }
        }

        static void CropImage(string filename, bool overwrite)
        {
            // Load the bitmap
            using Bitmap originalBitmap = Bitmap.FromFile(filename) as Bitmap;

            // Find the min/max non-white/transparent pixels
            Point min = new Point(int.MaxValue, int.MaxValue);
            Point max = new Point(int.MinValue, int.MinValue);

            using (BmpPixelSnoop fbmp = new BmpPixelSnoop(originalBitmap))
            {
                Color topLeft = fbmp.GetPixel(0, 0);

                for (int x = 0; x < fbmp.Width; ++x)
                {
                    for (int y = 0; y < fbmp.Height; ++y)
                    {
                        Color pixelColor = fbmp.GetPixel(x, y);
                        if (!(pixelColor.R == topLeft.R && pixelColor.G == topLeft.G && pixelColor.B == topLeft.B))
                        {
                            if (x < min.X) min.X = x;
                            if (y < min.Y) min.Y = y;

                            if (x > max.X) max.X = x;
                            if (y > max.Y) max.Y = y;
                        }
                    }
                }
            }

            Console.WriteLine("Cropping from " + min + " to " + max);

            // Create a new bitmap from the crop rectangle
            Rectangle cropRectangle = new Rectangle(min.X, min.Y, max.X - min.X, max.Y - min.Y);
            Bitmap newBitmap = new Bitmap(cropRectangle.Width, cropRectangle.Height);
            using (Graphics g = Graphics.FromImage(newBitmap))
            {
                g.DrawImage(originalBitmap, new Rectangle(0, 0, newBitmap.Width, newBitmap.Height),
                        cropRectangle, GraphicsUnit.Pixel);
            }

            var extension = Path.GetExtension(filename).ToLower();
            var saveAs = filename;
            if (!overwrite)
            {
                var basename = Path.GetFileNameWithoutExtension(filename) + "_Crop" + new Random().Next(100);
                saveAs = Path.Combine(Path.GetDirectoryName(filename), basename + extension);
            }

            if (extension == ".png")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Png);
            else if (extension == ".jpg" || extension == ".jpeg")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Jpeg);
            else if (extension == ".bmp")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Bmp);
            else if (extension == ".gif")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Gif);
        }
    }

    unsafe class BmpPixelSnoop : IDisposable
    {
        // A reference to the bitmap to be wrapped
        private readonly Bitmap wrappedBitmap;

        // The bitmap's data (once it has been locked)
        private BitmapData data = null;

        // Pointer to the first pixel
        private readonly byte* scan0;

        // Number of bytes per pixel
        private readonly int depth;

        // Number of bytes in an image row
        private readonly int stride;

        // The bitmap's width
        private readonly int width;

        // The bitmap's height
        private readonly int height;

        /// <summary>
        /// Constructs a BmpPixelSnoop object, the bitmap
        /// object to be wraped is passed as a parameter.
        /// </summary>
        /// <param name="bitmap">The bitmap to snoop</param>
        public BmpPixelSnoop(Bitmap bitmap)
        {
            wrappedBitmap = bitmap ?? throw new ArgumentException("Bitmap parameter cannot be null", "bitmap");

            // Currently works only for: PixelFormat.Format32bppArgb
            if (wrappedBitmap.PixelFormat != PixelFormat.Format32bppArgb)
                throw new System.ArgumentException("Only PixelFormat.Format32bppArgb is supported", "bitmap");

            // Record the width & height
            width = wrappedBitmap.Width;
            height = wrappedBitmap.Height;

            // So now we need to lock the bitmap so that we can gain access
            // to it's raw pixel data.  It will be unlocked when this snoop is 
            // disposed.
            var rect = new Rectangle(0, 0, wrappedBitmap.Width, wrappedBitmap.Height);

            try
            {
                data = wrappedBitmap.LockBits(rect, ImageLockMode.ReadWrite, wrappedBitmap.PixelFormat);
            }
            catch (Exception ex)
            {
                throw new System.InvalidOperationException("Could not lock bitmap, is it already being snooped somewhere else?", ex);
            }

            // Calculate number of bytes per pixel
            depth = Bitmap.GetPixelFormatSize(data.PixelFormat) / 8; // bits per channel

            // Get pointer to first pixel
            scan0 = (byte*)data.Scan0.ToPointer();

            // Get the number of bytes in an image row
            // this will be used when determining a pixel's
            // memory address.
            stride = data.Stride;
        }

        /// <summary>
        /// Disposes BmpPixelSnoop object
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes BmpPixelSnoop object, we unlock
        /// the wrapped bitmap.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (wrappedBitmap != null)
                    wrappedBitmap.UnlockBits(data);
            }
            // free native resources if there are any.
        }

        /// <summary>
        /// Calculate the pointer to a pixel at (x, x)
        /// </summary>
        /// <param name="x">The pixel's x coordinate</param>
        /// <param name="y">The pixel's y coordinate</param>
        /// <returns>A byte* pointer to the pixel's data</returns>
        private byte* PixelPointer(int x, int y)
        {
            return scan0 + y * stride + x * depth;
        }

        /// <summary>
        /// Snoop's implemetation of GetPixel() which is similar to
        /// Bitmap's GetPixel() but should be faster.
        /// </summary>
        /// <param name="x">The pixel's x coordinate</param>
        /// <param name="y">The pixel's y coordinate</param>
        /// <returns>The pixel's colour</returns>
        public System.Drawing.Color GetPixel(int x, int y)
        {
            // Better do the 'decent thing' and bounds check x & y
            if (x < 0 || y < 0 || x >= width || y >= height)
                throw new ArgumentException("x or y coordinate is out of range");

            int a, r, g, b;

            // Get a pointer to this pixel
            byte* p = PixelPointer(x, y);

            // Pull out its colour data
            b = *p++;
            g = *p++;
            r = *p++;
            a = *p;

            // And return a color value for it (this is quite slow
            // but allows us to look like Bitmap.GetPixel())
            return System.Drawing.Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// Sets the passed colour to the pixel at (x, y)
        /// </summary>
        /// <param name="x">The pixel's x coordinate</param>
        /// <param name="y">The pixel's y coordinate</param>
        /// <param name="col">The value to be assigned to the pixel</param>
        public void SetPixel(int x, int y, System.Drawing.Color col)
        {
            // Better do the 'decent thing' and bounds check x & y
            if (x < 0 || y < 0 || x >= width || y >= width)
                throw new ArgumentException("x or y coordinate is out of range");

            // Get a pointer to this pixel
            byte* p = PixelPointer(x, y);

            // Set the data
            *p++ = col.B;
            *p++ = col.G;
            *p++ = col.R;
            *p = col.A;
        }

        /// <summary>
        /// The bitmap's width
        /// </summary>
        public int Width { get { return width; } }

        // The bitmap's height
        public int Height { get { return height; } }
    }
}
