 using System;
using System.Data;
using System.Data.SqlClient;
using System.Configuration;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Drawing.Drawing2D;
using System.Web;
using NLog;

namespace UpdateWines
{
    public class Program
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        int delta = 2;
        public static void Main(string[] args)
        {
            string msg = string.Format("Time: {0}", DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt"));
            logger.Info(msg);
            logger.Info("-------------------------------------------------------");
            try
            {
                List<WineDetails> WineList = new List<WineDetails>();
                string str = ConfigurationManager.ConnectionStrings["DBConnection"].ConnectionString;
                int maxEnoID = 0;
                int maxPPId = 0;
                DataTable dt = new DataTable();
                using (SqlConnection con = new SqlConnection(str))
                {
                    using (SqlCommand cmd = new SqlCommand("CheckMaxWineIdAlt", con))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Connection = con;
                        con.Open();
                        logger.Info("Connection opened");
                        SqlDataAdapter da = new SqlDataAdapter(cmd);
                        DataSet ds = new DataSet();
                        da.Fill(ds);
                        logger.Info("Dataset obtained");
                        if (ds != null && ds.Tables.Count > 0)
                        {
                            if (ds.Tables[0].Rows.Count > 0)
                                maxEnoID = Convert.ToInt32(ds.Tables[2].Rows[0]["MaxId"]);
                            if (ds.Tables[1].Rows.Count > 0)
                                maxPPId = Convert.ToInt32(ds.Tables[3].Rows[0]["MaxId"]);

                            dt = ds.Tables[0];
                            dt.Merge(ds.Tables[1]);

                            if (dt.Rows.Count > 0)
                            {
                                foreach (DataRow dr in dt.Rows)
                                {
                                    WineDetails WineObj = new WineDetails();
									WineObj.BarCode = dr["BarCode"].ToString();
                                  //  WineObj.WineId = Convert.ToInt32(dr["WineId"]);
                                    WineObj.WineName = dr["WineName"].ToString();
                                    WineObj.Vintage = dr["Vintage"].ToString();
                                    WineObj.Store = Convert.ToInt32(dr["store"]);
                                    WineList.Add(WineObj);
                                }
                                con.Close();
                            }
                        }
                    }

                }

                Program p = new Program();
                int success = 0;
                foreach (WineDetails obj in WineList)
                {
                    Image img = p.GetFile(obj.WineName, obj.Vintage);
                    logger.Info("Obtained Image for " + obj.WineName + ". Uploading Image..");
                    success = p.UploadImage(img, obj.BarCode, obj.Store);
                }

                if (WineList.Count > 0)
                {
                    using (SqlConnection con = new SqlConnection(str))
                    {
                        if (maxEnoID > 0)
                        {
                            using (SqlCommand cmd = new SqlCommand("update updateWine set MaxWineID=@wineId where storeId = 1", con))
                            {
                                cmd.Parameters.AddWithValue("@wineId", maxEnoID);
                                cmd.Connection = con;
                                con.Open();
                                logger.Info("Updating maximum wine id for Wall DB");
                                cmd.ExecuteNonQuery();
                                con.Close();
                            }
                        }
                        if (maxPPId > 0)
                        {
                            using (SqlCommand cmd = new SqlCommand("update updateWine set MaxWineID=@wineId where storeId = 2", con))
                            {
                                cmd.Parameters.AddWithValue("@wineId", maxPPId);
                                cmd.Connection = con;
                                con.Open();
                                logger.Info("Updating maximum wine id for Point pleasent DB");
                                cmd.ExecuteNonQuery();
                                con.Close();
                            }
                        }

                    }
                }

                p.getImagesFromDrive();
            }
            catch(Exception ex)
            {
                string path = ConfigurationManager.AppSettings["ErrorLog"];
                string message = string.Format("Time: {0}", DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt"));
                message += Environment.NewLine;
                message += "-----------------------------------------------------------";
                message += Environment.NewLine;
                message += string.Format("Message: {0}", ex.Message);
                message += Environment.NewLine;
                message += string.Format("StackTrace: {0}", ex.StackTrace);
                message += Environment.NewLine;
                message += string.Format("Source: {0}", ex.Source);
                message += Environment.NewLine;
                message += string.Format("TargetSite: {0}", ex.TargetSite.ToString());
                message += Environment.NewLine;
                message += "-----------------------------------------------------------";
                message += Environment.NewLine;
                System.IO.Directory.CreateDirectory(path);
                using (StreamWriter writer = new StreamWriter(path + "Error.txt", true))
                {
                    writer.WriteLine(message);
                    writer.Close();
                }
            }      

        }


        private string GetHtmlCode(string wineName, string Vintage)
        {
            string searchText = "bottle image for " + wineName + " " + Vintage;

            string url = "https://www.google.com/search?q=" + searchText + "&tbm=isch";
            string data = "";

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Accept = "text/html, application/xhtml+xml, */*";
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";

            var response = (HttpWebResponse)request.GetResponse();

            using (Stream dataStream = response.GetResponseStream())
            {
                if (dataStream == null)
                    return "";
                using (var sr = new StreamReader(dataStream))
                {
                    data = sr.ReadToEnd();
                }
            }
            return data;
        }

        private List<string> GetUrls(string html)
        {
            var urls = new List<string>();

            int ndx = html.IndexOf("\"ou\"", StringComparison.Ordinal);

            int count = 0;
            while (ndx >= 0 && count < 10)
            {
                ndx = html.IndexOf("\"", ndx + 4, StringComparison.Ordinal);
                ndx++;
                int ndx2 = html.IndexOf("\"", ndx, StringComparison.Ordinal);
                string url = html.Substring(ndx, ndx2 - ndx);
                urls.Add(url);
                ndx = html.IndexOf("\"ou\"", ndx2, StringComparison.Ordinal);
                count++;
            }
            return urls;
        }

        private byte[] GetImage(string url)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                var response = (HttpWebResponse)request.GetResponse();

                using (Stream dataStream = response.GetResponseStream())
                {
                    if (dataStream == null)
                        return null;
                    using (var sr = new BinaryReader(dataStream))
                    {
                        byte[] bytes = sr.ReadBytes(100000000);

                        return bytes;
                    }
                }

            }
            catch (Exception)
            {
                //throw;
            }
            return null;
        }

        public Image GetFile(string wineName, string Vintage)
        {
            logger.Info("Getting file for "+wineName+" ....");
            string html = GetHtmlCode(wineName, Vintage);
            List<string> urls = GetUrls(html);
            Image img;
            Bitmap bitmp = null;
            for (int i = 0; i < urls.Count; i++)
            {
                string luckyUrl = urls[i];

                byte[] image = GetImage(luckyUrl);
                if (image == null)
                    continue;
                try
                {
                    using (var ms = new MemoryStream(image))
                    {
                        img = Image.FromStream(ms);
                    }
                    Bitmap x = img.Clone() as Bitmap;
                    int ret = possibleCandidate(x);
                    if (ret == 1)
                    {
                        //Convert Trans to White
                        Bitmap newBitmap = new Bitmap(x.Width, x.Height);
                        Color actualColor;
                        for (int j = 0; j < x.Height; j++)
                        {
                            for (int k = 0; k < x.Width; k++)
                            {
                                actualColor = x.GetPixel(k, j);

                                if (actualColor.A == 0 && actualColor.R == 0 && actualColor.G == 0 && actualColor.B == 0)
                                    newBitmap.SetPixel(k, j, Color.White);
                                else
                                    newBitmap.SetPixel(k, j, actualColor);
                            }
                        }
                        return newBitmap;
                    }
                    else if (ret == 2 && bitmp == null)
                    {
                        //bitmp = MakeTransparent(x);
                        Bitmap newBitmap = new Bitmap(x.Width, x.Height);
                        for (int i1 = 0; i1 < x.Width; i1++)
                        {
                            for (int j = 0; j < x.Height; j++)
                            {
                                newBitmap.SetPixel(i1, j, x.GetPixel(i1, j));
                            }
                        }
                        bitmp = newBitmap;
                    }
                }
                catch (Exception ex)
                {
                    //string message = string.Format("Time: {0}", DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt"));
                    //string path = Server.MapPath("~/ErrorLog.txt");
                    //using (StreamWriter writer = new StreamWriter(path, true))
                    //{
                    //    writer.WriteLine(message);
                    //    writer.Close();
                    //}
                }
            }
            return (Image)bitmp;
        }

        private int possibleCandidate(Bitmap scrBitmap)
        {
            Color actualColor;
            //for (int i = 0; i < scrBitmap.Width; i++)
            //{
            //for (int j = 0; j < scrBitmap.Height; j++)
            //{
            //if (scrBitmap.Height / scrBitmap.Width < 3)
            //    return 0;

            actualColor = scrBitmap.GetPixel(0, 0);
            if (actualColor.A == 0 && actualColor.R == 0 && actualColor.G == 0 && actualColor.B == 0)
            {
                return 1;
            }
            if (MatchColor(actualColor))
            {
                return 2;
            }
            //}
            //}

            return 0;
        }

        private Bitmap MakeTransparent(Bitmap scrBitmap)
        {
            //You can change your new color here. Red,Green,LawnGreen any..
            Color actualColor;
            //make an empty bitmap the same size as scrBitmap
            Bitmap newBitmap = new Bitmap(scrBitmap.Width, scrBitmap.Height);
            for (int i = 0; i < scrBitmap.Width; i++)
            {
                for (int j = 0; j < scrBitmap.Height; j++)
                {
                    newBitmap.SetPixel(i, j, scrBitmap.GetPixel(i, j));
                }
            }

            for (int j = 0; j < scrBitmap.Height; j++)
            {
                for (int i = 0; i < scrBitmap.Width; i++)
                {
                    actualColor = scrBitmap.GetPixel(i, j);

                    if (MatchColor(actualColor))
                        newBitmap.SetPixel(i, j, Color.Transparent);
                    else
                        break; //newBitmap.SetPixel(i, j, actualColor);
                }
                for (int i = scrBitmap.Width - 1; i >= 0; i--)
                {
                    actualColor = scrBitmap.GetPixel(i, j);

                    if (MatchColor(actualColor))
                        newBitmap.SetPixel(i, j, Color.Transparent);
                    else
                        break; //newBitmap.SetPixel(i, j, actualColor);
                }
            }


            return newBitmap;
        }

        private bool MatchColor(Color actualColor)
        {
            if (255 - actualColor.A <= delta && 255 - actualColor.R <= delta && 255 - actualColor.G <= delta && 255 - actualColor.B <= delta)
                return true;
            else
                return false;
        }

        private int UploadImage(Image BottleImage, string BarCode,int store)
        {
            string conStrings = ConfigurationManager.ConnectionStrings["AzureStorageConnection"].ConnectionString;
            CloudStorageAccount storageaccount = CloudStorageAccount.Parse(conStrings);
            CloudBlobClient blobClient = storageaccount.CreateCloudBlobClient();
            CloudBlobContainer container = null;
            if (store == 1)
                container = blobClient.GetContainerReference("barcodewall");
            else if(store == 2)
                container = blobClient.GetContainerReference("barcodepp");
            container.CreateIfNotExists();
            //For BottleImages
            CloudBlockBlob blob = container.GetBlockBlobReference(BarCode + ".jpg");
            if (BottleImage != null)
            {
                Image ImageForBottle = ResizeImage(BottleImage, BottleImage.Width, BottleImage.Height,250,300);
                string path = @"C:\soumik\personal\New folder\" + BarCode + ".jpg";
                ImageForBottle.Save(path);
                logger.Info("Uploading Image to Blob!");
                using (var fs = System.IO.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    blob.UploadFromStream(fs);
                    fs.Close();
                }
                logger.Info("Image uploaded");
                File.Delete(path);

                //For BottleDetailsImages
                if(store == 1)
                    container = blobClient.GetContainerReference("barcodewalldetail");
                else if (store == 2)
                    container = blobClient.GetContainerReference("barcodeppdetail");
                blob = container.GetBlockBlobReference(BarCode + ".jpg");
                ImageForBottle = ResizeImage(BottleImage, BottleImage.Width, BottleImage.Height, 750, 900);
                ImageForBottle.Save(path);
                logger.Info("Uploading Details image to Blob!");
                using (var fs = System.IO.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    blob.UploadFromStream(fs);
                    fs.Close();
                }
                logger.Info("Image uploaded");
                File.Delete(path);
                ImageForBottle.Dispose();
                BottleImage.Dispose();           
                return 1;
            }
            else
            {
                logger.Info("Image not found for "+BarCode);
                return 0;
            }
        }

        public static Bitmap ResizeImage(Image image, int width, int height,int desiredWidth,int desiredHeight)
        {
            //float ratio = ((float)240) / height;
            //ratio = ratio / 2;
            float nPercent = 0;
            float nPercentW = 0;
            float nPercentH = 0;

            nPercentW = ((float)desiredWidth / (float)width);
            nPercentH = ((float)desiredHeight / (float)height);

            if (nPercentH < nPercentW)
                nPercent = nPercentH;
            else
                nPercent = nPercentW;
            float ratio = nPercent;
            var destRect = new Rectangle(0, 0, Convert.ToInt32(width * ratio), Convert.ToInt32(height * ratio));
            var destImage = new Bitmap(Convert.ToInt32(width * ratio), Convert.ToInt32(height * ratio));

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        private void getImagesFromDrive()
        {
            string path = ConfigurationManager.AppSettings["GoogleDrivePath"];
            DirectoryInfo di = new DirectoryInfo(path);
            FileInfo[] Images = null; 
            bool IsPresent = di.GetFiles("*.jpg").Any();
            if(IsPresent)
            {
                 Images = di.GetFiles("*.jpg");
                 for(int i=0;i<Images.Length;i++)
                 {
                    string[] wineName = Images[i].Name.Split('.');
                    int sku = int.Parse(wineName[0]);
                    logger.Info("Uploading image from Drive!");
                    string BarCode = getWineId(sku);
                    if (BarCode != "")
                    {
                        string fullPath = path + "\\" + Images[i].Name;
                        logger.Info("Uploading Image from drive for :"+Images[i].Name);
                        UploadImage(Image.FromFile(fullPath), BarCode,1);
                        File.Delete(fullPath);
                    }
                 }
            }

            
        }

        private string getWineId(int sku)
        {
            string BarCode = "";
            string str = ConfigurationManager.ConnectionStrings["DBConnection"].ConnectionString;
            using (SqlConnection con = new SqlConnection(str))
            {
                using (SqlCommand cmd = new SqlCommand("GetWineIdForSKUAlt", con))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@sku",sku);
                    cmd.Connection = con;
                    con.Open();
                    BarCode = cmd.ExecuteScalar().ToString();
                }

            }
            return BarCode;

        }
    }
}
