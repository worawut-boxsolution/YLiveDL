using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YLiveDL.Util
{
    public static class StringExtensions
    {
        public static string ReplaceInvalidFileNameChars(this string fileName, string replacement = "_")
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName
                .Select(ch => invalidChars.Contains(ch) ? replacement[0] : ch)
                .ToArray());
        }
    }
}
