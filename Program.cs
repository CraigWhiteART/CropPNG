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
            IsConsole = GetConsoleWindow() != IntPtr.Zero;

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

            List<FileInfo> validPaths = new List<FileInfo>();
            string currentPath = "";
            for (int i = 0; i < args_list.Count; i++)
            {
                var segment = args_list[i];
                //check if starts with a drive letter
                if (segment.Length >= 2 && segment[1] == ':')
                {
                    if(currentPath.Length > 0)
                        Alert(currentPath + " does not exist");
                    currentPath = segment;
                }
                else
                {
                    if (currentPath.Length > 0) currentPath += " ";
                    currentPath += segment;
                }
                var finfo = new FileInfo(currentPath);
                if (finfo.Exists)
                {
                    validPaths.Add(finfo);
                    currentPath = "";
                }
            }
            if (currentPath.Length > 0)
                Alert(currentPath + " does not exist");
            foreach (var finfo in validPaths)
            {
                var filename = finfo.FullName;
                //string filename = string.Join(" ", args_list);
                //FileInfo finfo = new FileInfo(filename);
                if (!finfo.Exists)
                {
                    Alert(finfo.FullName + " does not exist");
                    continue;
                }
                if (finfo.Extension.ToUpperInvariant().Trim('.') != "PNG")
                {
                    Alert(finfo.Extension + " not supported");
                    continue;
                }
                try
                {
                    CropImage(finfo, overwrite);
                }
                catch (Exception ex)
                {
                    Alert(ex.Message);
                }
            }
        }

        static void Alert(string message)
        {
            if (IsConsole) { WriteLine(message); }
            else { MessageBox.Show(message); }
        }

        static void WriteLine(string message)
        {
            if (Debugger.IsAttached) Debug.WriteLine(message);
            Console.WriteLine(message);
        }

        static void CropImage(FileInfo finfo, bool overwrite, int padding = 0)
        {
            var lastWrite = finfo.LastWriteTimeUtc;
            var creationTime = finfo.CreationTimeUtc;

            Rectangle cropRectangle;
            Bitmap newBitmap = CropImage(finfo, padding, out cropRectangle);

            var extension = finfo.Extension.ToLower();
            var saveAs = finfo.FullName;
            var basename = Path.GetFileNameWithoutExtension(saveAs);
            if (!overwrite)
            {
                saveAs = Path.Combine(Path.GetDirectoryName(saveAs), basename + "_Crop" + extension);
            }

            if (extension == ".png")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Png);
            else if (extension == ".jpg" || extension == ".jpeg")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Jpeg);
            else if (extension == ".bmp")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Bmp);
            else if (extension == ".gif")
                newBitmap.Save(saveAs, System.Drawing.Imaging.ImageFormat.Gif);

            var saveFilename = Path.GetFileName(saveAs);
            WriteLine($"Saved \"{saveFilename}\" - Crop to {cropRectangle}");

            try
            {
                File.SetLastWriteTimeUtc(saveFilename, lastWrite.AddSeconds(1));
                File.SetCreationTimeUtc(saveFilename, creationTime.AddSeconds(1));
            }
            catch (Exception ex)
            {
                WriteLine(ex.Message);
            }
        }

        private static Bitmap CropImage(FileInfo finfo, int padding, out Rectangle cropRectangle)
        {
            // Load the bitmap
            using Bitmap originalBitmap = Bitmap.FromFile(finfo.FullName) as Bitmap;
            cropRectangle = GetCropBounds(originalBitmap);
            cropRectangle = PadRectangle(cropRectangle, padding, originalBitmap.Width, originalBitmap.Height);

            // Create a new bitmap from the crop rectangle
            Bitmap newBitmap = new Bitmap(cropRectangle.Width, cropRectangle.Height);
            using (Graphics g = Graphics.FromImage(newBitmap))
            {
                g.DrawImage(originalBitmap, new Rectangle(0, 0, newBitmap.Width, newBitmap.Height),
                        cropRectangle, GraphicsUnit.Pixel);
            }
            
            originalBitmap.Dispose();//Close the file

            return newBitmap;
        }

        static Rectangle GetCropBounds(Bitmap bmp)
        {
            using (var fbmp = new BmpPixelSnoop(bmp))
            {
                int w = fbmp.Width, h = fbmp.Height, bg = fbmp.GetPixel(0, 0).ToArgb();
                int top = 0, bottom = h - 1, left = 0, right = w - 1;

                // Move top edge down until a non–background pixel is found.
                while (top < h && Enumerable.Range(0, w).All(x => fbmp.GetPixel(x, top).ToArgb() == bg))
                    top++;

                // Move bottom edge up.
                while (bottom >= top && Enumerable.Range(0, w).All(x => fbmp.GetPixel(x, bottom).ToArgb() == bg))
                    bottom--;

                // Move left edge right.
                while (left < w && Enumerable.Range(top, bottom - top + 1).All(y => fbmp.GetPixel(left, y).ToArgb() == bg))
                    left++;

                // Move right edge left.
                while (right >= left && Enumerable.Range(top, bottom - top + 1).All(y => fbmp.GetPixel(right, y).ToArgb() == bg))
                    right--;

                return new Rectangle(left, top, right - left + 1, bottom - top + 1);
            }
        }

        static Rectangle PadRectangle(Rectangle rect, int padding, int imageWidth, int imageHeight)
        {
            // Calculate new coordinates with padding, ensuring they don't go beyond the image bounds.
            int newX = Math.Max(0, rect.X - padding);
            int newY = Math.Max(0, rect.Y - padding);
            int newRight = Math.Min(imageWidth, rect.Right + padding);
            int newBottom = Math.Min(imageHeight, rect.Bottom + padding);

            return new Rectangle(newX, newY, newRight - newX, newBottom - newY);
        }
    }

    
}
