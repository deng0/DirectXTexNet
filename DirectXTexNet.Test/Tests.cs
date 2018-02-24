using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DirectXTexNet.Test.Util;
using NUnit.Framework;

namespace DirectXTexNet.Test
{
    public class Tests
    {
        [Test]
        public void CheckOutOfRange()
        {
            var path = GetImagePath("ThisIsATest.tga");

            using (var image = TexHelper.Instance.LoadFromTGAFile(path))
            {
                var index = image.ComputeImageIndex(100, 100, 100); // is out of range
                Assert.AreEqual(TexHelper.Instance.IndexOutOfRange, index);
            }
        }

        private ScratchImage createTempFromMips(ScratchImage orig, out WeakReference weak)
        {
            var mips = orig.GenerateMipMaps(TEX_FILTER_FLAGS.FANT, 0);
            weak = new WeakReference(mips);
            uint count =(uint) mips.GetImageCount();
            Image[] arr = new Image[count];
            for (uint i = 0; i < count; i++)
            {
                arr[i] = mips.GetImage(i);
            }

            return TexHelper.Instance.InitializeTemporary(arr, mips.GetMetadata());
        }

        [Test]
        public void Finalizers()
        {
            var path = GetImagePath("ThisIsATest.tga");

            using (var image = TexHelper.Instance.LoadFromTGAFile(path))
            {
                // create Mips within other method call and wrap its image with TempScratchImages
                WeakReference weakRef1;
                WeakReference weakRef2;
                ScratchImage temp1 = this.createTempFromMips(image, out weakRef1);
                ScratchImage temp2 = this.createTempFromMips(image, out weakRef2);

                // tests if the TempScratchImages prevents the mips from being finalized
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Assert.True(weakRef1.IsAlive);
                Assert.True(weakRef2.IsAlive);

                // now one TempScratchImage is disposed and its underlying mips should be finalized
                temp1.Dispose();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Assert.False(weakRef1.IsAlive);
                Assert.True(weakRef2.IsAlive);

                // dispose the other (but GC would finalize it anyway)
                temp2.Dispose();
            }
        }

        [Test]
        public unsafe void SaveAndLoadToMemory()
        {
            var path = GetImagePath("ThisIsATest.png");

            using (var image = TexHelper.Instance.LoadFromWICFile(path, WIC_FLAGS.NONE))
            {
                Guid guid = TexHelper.Instance.GetWICCodec(WICCodecs.PNG);
                //image.SaveToWICFile(0, WIC_FLAGS.NONE, guid, Path.Combine(this.GetOutputFolder(), "copy.png"));
                using (UnmanagedMemoryStream memStream = image.SaveToWICMemory(0, WIC_FLAGS.NONE, guid))
                {
                    using (Bitmap bmp = (Bitmap)System.Drawing.Image.FromStream(memStream))
                    {
                        this.AssertEqual(image, bmp);

                        memStream.Seek(0, SeekOrigin.Begin);

                        using (ScratchImage fromMem = TexHelper.Instance.LoadFromWICMemory(
                            new IntPtr(memStream.PositionPointer),
                            (uint)memStream.Length,
                            WIC_FLAGS.FORCE_RGB)) // it seems DirectXTex writes image in different format
                        {
                            //fromMem.SaveToWICFile(0, WIC_FLAGS.NONE, guid, Path.Combine(this.GetOutputFolder(), "copy2.png"));
                            this.AssertEqual(fromMem, bmp);
                        }
                    }
                }
            }
        }

        [Test]
        public void EvaluateImage()
        {
            var path = GetImagePath("ThisIsATest.tga");

            using (var image = TexHelper.Instance.LoadFromTGAFile(path))
            {
                var metaData = image.GetMetadata();

                ByteSumEvaluator evaluator = new ByteSumEvaluator(4);
                image.EvaluateImage(evaluator.EvaluatePixels);
                Assert.AreEqual(1027555.0, evaluator.Sums[0]);
                Assert.AreEqual(830972.0, evaluator.Sums[1]);
                Assert.AreEqual(838518.0, evaluator.Sums[2]);
                Assert.AreEqual(255.0 * metaData.Width * metaData.Height, evaluator.Sums[3]); // alpha is always 255.0
            }
        }

        class ByteSumEvaluator
        {
            public readonly double[] Sums;

            public ByteSumEvaluator(int channelCount)
            {
                this.Sums = new double[channelCount];
            }

            public unsafe void EvaluatePixels(IntPtr pixels, UIntPtr width, UIntPtr y)
            {
                float* ptr = (float*)pixels.ToPointer();

                ulong u = 0;
                ulong widthV = width.ToUInt64();

                //float[] data = new float[widthV * (ulong)this.sums.Length];
                //Marshal.Copy(pixels, data, 0, data.Length);

                for (ulong i = 0; i < widthV; i++)
                {
                    for (int j = 0; j < this.Sums.Length; j++)
                    {
                        this.Sums[j] += Math.Round(255f * ptr[u++], 3); // rounding because of floating point precision problems
                    }
                }
            }
        }

        [Test]
        public void TransformImage()
        {
            var path = GetImagePath("ThisIsATest.tga");

            using (var image = TexHelper.Instance.LoadFromTGAFile(path))
            {
                //Guid guid = TexHelper.GetWICCodec(WICCodecs.JPEG);
                //image.SaveToWICFile(0, WIC_FLAGS.NONE, guid, Path.Combine(this.GetOutputFolder(), "orig.jpg"));

                ByteTransformation transform = new ByteTransformation(4);
                using (var result = image.TransformImage(transform.TransformPixels))
                {
                    using (var result2 = result.TransformImage(transform.TransformPixels))
                    {
                        Image image1 = image.GetImage(0);
                        Image image2 = result2.GetImage(0);
                        float mse;
                        MseV mseV;
                        TexHelper.Instance.ComputeMSE(image1, image2, out mse, out mseV, CMSE_FLAGS.DEFAULT);

                        Assert.AreEqual(0.0, mse);
                    }
                }
            }
        }

        class ByteTransformation
        {
            public readonly int ChannelCount;

            public ByteTransformation(int channelCount)
            {
                this.ChannelCount = channelCount;
            }

            public unsafe void TransformPixels(IntPtr outPixels, IntPtr inPixels, UIntPtr width, UIntPtr y)
            {
                float* outPtr = (float*)outPixels.ToPointer();
                float* inPtr = (float*)inPixels.ToPointer();

                ulong u = 0;
                ulong widthV = width.ToUInt64();

                //float[] data = new float[widthV * (ulong)this.channelCount];
                //Marshal.Copy(inPixels, data, 0, data.Length);

                for (ulong i = 0; i < widthV; i++)
                {
                    for (int j = 0; j < this.ChannelCount - 1; j++)
                    {
                        outPtr[u] = 1.0f - inPtr[u];
                        u++;
                    }
                    outPtr[u] = inPtr[u];
                    u++;
                }
            }
        }


        // Test for WIC loading path, which we can compare to the result of loading using System.Drawing.
        [TestCase("ThisIsATest.png")]
        [TestCase("ThisIsATest.bmp")]
        [TestCase("ThisIsATest.jpg")]
        public void LoadWIC(string filename)
        {
            var path = GetImagePath(filename);

            // Load image with DirectXTex.
            using (var image = TexHelper.Instance.LoadFromWICFile(path, WIC_FLAGS.NONE))
            {
                // Load the same image with System.Drawing.
                using (var expected = new Bitmap(path))
                {
                    // Assert that they match.
                    this.AssertEqual(image, expected);
                }
            }
        }

        [Test]
        public void ResizeMipsAndCompress()
        {
            var path = GetImagePath("ThisIsATest.tga");

            // leave one logical core idle
            TexHelper.Instance.SetOmpMaxThreadCount(Math.Max(1, Environment.ProcessorCount - 1));

            Stopwatch watch = new Stopwatch();
            watch.Start();

            using (var origImage = TexHelper.Instance.LoadFromTGAFile(path))
            {
                Console.WriteLine("Open " + watch.ElapsedMilliseconds);
                watch.Restart();

                using (var image = origImage.Resize(4096, 4096, TEX_FILTER_FLAGS.LINEAR))
                {
                    Console.WriteLine("Resize " + watch.ElapsedMilliseconds);
                    watch.Restart();

                    Guid guid = TexHelper.Instance.GetWICCodec(WICCodecs.JPEG);
                    string jpgFilePath = Path.Combine(this.GetOutputFolder(), "resized.jpg");
                    image.SaveToWICFile(0, WIC_FLAGS.NONE, guid, jpgFilePath);
                    Console.WriteLine("Save Jpeg " + watch.ElapsedMilliseconds);
                    watch.Restart();

                    using (TexHelper.Instance.LoadFromWICFile(jpgFilePath, WIC_FLAGS.NONE))
                    {
                        Console.WriteLine("Load Jpeg " + watch.ElapsedMilliseconds);
                        watch.Restart();
                    }

                    //TexMetadata metadata = image.GetMetadata();
                    //using (ScratchImage mipChain = image.CreateCopyWithEmptyMipMaps(0, metadata.Format, CP_FLAGS.NONE, false))
                    using (ScratchImage mipChain = image.GenerateMipMaps(TEX_FILTER_FLAGS.FANT, 0))
                    {
                        Console.WriteLine("MipMaps " + watch.ElapsedMilliseconds);
                        watch.Restart();

                        using (var comp = mipChain.Compress(DXGI_FORMAT.BC1_UNORM, TEX_COMPRESS_FLAGS.PARALLEL, 0.5f))
                        {
                            Console.WriteLine("Compress " + watch.ElapsedMilliseconds);
                            watch.Restart();

                            string compFilePath = Path.Combine(this.GetOutputFolder(), "compressed.dds");
                            comp.SaveToDDSFile(DDS_FLAGS.NONE, compFilePath);
                            Console.WriteLine("Save Compressed DDS " + watch.ElapsedMilliseconds);
                            watch.Restart();

                            using (var comp2 = TexHelper.Instance.LoadFromDDSFile(compFilePath, DDS_FLAGS.NONE))
                            {
                                Console.WriteLine("Load Compressed DDS " + watch.ElapsedMilliseconds);
                                watch.Restart();
                            }
                        }
                    }
                }
            }
        }

        // Test for TGA loading path.
        [Test]
        public void LoadTga()
        {
            var path = GetImagePath("ThisIsATest.tga");
            var refPath = GetImagePath("ThisIsATest.png");

            // Load image with DirectXTex.
            using (var image = TexHelper.Instance.LoadFromTGAFile(path))
            {
                // Can't load a tga with System.Drawing, so load an identical png.
                using (var expected = new Bitmap(refPath))
                {
                    // Assert that they match.
                    this.AssertEqual(image, expected);
                }
            }
        }

        // Test for DDS loading path.
        [Test]
        public void LoadDds()
        {
            var path = GetImagePath("ThisIsATest.dds");
            var refPath = GetImagePath("ThisIsATest.png");

            // Load image with DirectXTex.
            using (var image = TexHelper.Instance.LoadFromDDSFile(path, DDS_FLAGS.NONE))
            {
                // Can't load a dds with System.Drawing, so load an identical png.
                using (var expected = new Bitmap(refPath))
                {
                    // Assert that they match.
                    this.AssertEqual(image, expected);
                }
            }

            // Load image with DirectXTex.
            TexMetadata meta = TexHelper.Instance.GetMetadataFromDDSFile(path, DDS_FLAGS.NONE);

            // Can't load a dds with System.Drawing, so load an identical png.
            using (var expected = new Bitmap(refPath))
            {
                // Assert that they match.
                AssertEqual(meta, expected);
            }
        }

        private void AssertEqual(TexMetadata metaData, Bitmap expected)
        {
            Assert.AreEqual((ulong)expected.Width, metaData.Width);
            Assert.AreEqual((ulong)expected.Height, metaData.Height);
            Assert.AreEqual(1UL, metaData.Depth);
            Assert.AreEqual(TEX_DIMENSION.TEXTURE2D, metaData.Dimension);
            Assert.AreEqual(1UL, metaData.ArraySize);
            Assert.AreEqual(1UL, metaData.MipLevels);

            // DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
            Assert.That((uint)metaData.Format, Is.EqualTo(28).Or.EqualTo(29));
        }

        private void AssertEqual(ScratchImage image, Bitmap expected)
        {
            // Check various meta-data.
            var metaData = image.GetMetadata();
            AssertEqual(metaData, expected);

            // Check raw contents match. (This test only works for specific images)
            var img = image.GetImage(0);
            var expectedBytes = expected.GetRawBytesRGBA();
            var readBytes = new byte[expectedBytes.Length];
            Marshal.Copy(img.Pixels, readBytes, 0, readBytes.Length);
            Assert.That(readBytes, Is.EqualTo(expectedBytes));
        }

        private string GetOutputFolder()
        {
            string outFolder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "OutImages");
            if (!Directory.Exists(outFolder))
            {
                Directory.CreateDirectory(outFolder);
            }

            return outFolder;
        }

        private string GetImagePath(string filename)
        {
            // Directory we're looking for.
            var dirToFind = Path.Combine(@"DirectXTexNet.Test", "Images");

            // Search up directory tree starting at assembly path looking for 'Images' dir.
            var searchPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            while (true)
            {
                var testPath = Path.Combine(searchPath, dirToFind);
                if (Directory.Exists(testPath))
                {
                    // Found it!
                    return Path.Combine(testPath, filename);
                }

                // Move up one directory.
                var newSearchPath = Path.GetFullPath(Path.Combine(searchPath, ".."));
                if (newSearchPath == searchPath)
                {
                    // Didn't move up, so we're at the root.
                    throw new FileNotFoundException($"Could not find '{dirToFind}' directory.");
                }
                searchPath = newSearchPath;
            }
        }
    }
}
