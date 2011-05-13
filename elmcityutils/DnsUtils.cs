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

namespace ElmcityUtils
{
	public static class DnsUtils
	{

		public static string TryGetHostName(string name_or_address)
		{
			try
			{
				var host_entry = System.Net.Dns.GetHostEntry(name_or_address);
				return host_entry.HostName;
			}
			catch
			{
				return name_or_address;
			}
		}

		public static string TryGetHostAddr(string name_or_address)
		{
			try
			{
				var host_entry = System.Net.Dns.GetHostEntry(name_or_address);
				return host_entry.AddressList[0].ToString();
			}
			catch
			{
				return name_or_address;
			}
		}
	}
}
