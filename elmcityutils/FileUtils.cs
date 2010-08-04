/* ********************************************************************************
 *
 * Copyright 2010 Microsoft Corporation
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you
 * may not use this file except in compliance with the License. You may
 * obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0 
 * Unless required by applicable law or agreed to in writing, software distributed 
 * under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
 * CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 * specific language governing permissions and limitations under the License. 
 *
 * *******************************************************************************/

using System;
using System.IO;
using Ionic.Zip;

namespace ElmcityUtils
{
    public static class FileUtils
    {

        public static DirectoryInfo CreateLocalDirectoryUnderCurrent(string name)
        {
            var cd = Directory.GetCurrentDirectory();
            return Directory.CreateDirectory(string.Format(@"{0}\{1}", cd, name));
        }

        public static void UnzipFromUrlToCurrentDirectory(Uri zip_url)
        {
            var zip_response = HttpUtils.FetchUrl(zip_url);
            var zs = new MemoryStream(zip_response.bytes);
            var zip = ZipFile.Read(zs);
            var cd = Directory.GetCurrentDirectory();
            foreach (var entry in zip.Entries)
                entry.Extract(cd);
        }

        public static void UnzipFromUrlToCurrentDirectory(Uri zip_url, string existing_dir)
        {
            if (Directory.Exists(existing_dir))
                Directory.Delete(existing_dir, true);
            UnzipFromUrlToCurrentDirectory(zip_url);
        }

    }
}
