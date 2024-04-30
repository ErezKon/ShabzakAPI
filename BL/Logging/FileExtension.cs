using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Logging
{
    public static class FileExtension
    {
        public static void SafeCreate(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    if (!Directory.Exists(fileInfo.Directory.FullName))
                    {
                        Directory.CreateDirectory(fileInfo.Directory.FullName);
                    }
                    var file = File.Create(path);
                    file.Close();
                    file.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
