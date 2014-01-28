using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ElmcityUtils;
using System.Net;
using System.Diagnostics;

namespace CalendarAggregator
{
	[Serializable]
	public class WebRoleData
	{
		public static string procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
		public static int procid = System.Diagnostics.Process.GetCurrentProcess().Id;
		public static string domain_name = AppDomain.CurrentDomain.FriendlyName;
		public static int thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;

		// on startup, and then periodically, a renderer is constructed for each hub
		public Dictionary<string, CalendarRenderer> renderers = new Dictionary<string, CalendarRenderer>();

		//public Dictionary<string, Calinfo> calinfos = new Dictionary<string, Calinfo>(); // todo: remove this vestige 

		public List<string> where_ids = new List<string>();
		public List<string> what_ids = new List<string>();
		public List<string> region_ids = new List<string>();

		// on startup, and then periodically, this list of "ready" hubs is constructed
		// ready means that the hub has been added to the system, and there has been at 
		// least one successful aggregation run resulting in an output like:
		// http://elmcity.cloudapp.net/services/ID/html

		public List<string> ready_ids = new List<string>();

		// the stringified version of the list controls the namespace, under /services, [update: or under /] that the
		// service responds to. so when a new hub is added, say Peekskill, NY, with id peekskill, 
		// the /services/peekskill family of URLs won't become active until the hub joins the list of ready_ids
		public string str_ready_ids;


		public WebRoleData()
		{
			GenUtils.LogMsg("info", String.Format("WebRoleData: {0}, {1}, {2}, {3}", procname, procid, domain_name, thread_id), null);

			MakeWhereAndWhatAndRegionIdLists();

			var ids = Metadata.LoadHubIdsFromAzureTable();

			Parallel.ForEach(ids, id =>
			//foreach (var id in ids)
			{
				GenUtils.LogMsg("info", "GatherWebRoleData: readying: " + id, null);
				var cr = Utils.AcquireRenderer(id);
				lock (this.renderers)
				{
					this.renderers.Add(id, cr);
				}
				lock (this.ready_ids)
				{
					this.ready_ids.Add(id);
				}
			});
			//}

			// this pipe-delimited string defines allowed IDs in the /services/ID/... URL pattern
			this.str_ready_ids = String.Join("|", this.ready_ids.ToArray());

			GenUtils.LogMsg("info", "GatherWebRoleData: str_ready_ids: " + this.str_ready_ids, null);
		}

		private void MakeWhereAndWhatAndRegionIdLists()
		{
			this.where_ids = Metadata.LoadHubIdsFromAzureTableByType(HubType.where);
			var where_ids_as_str = string.Join(",", this.where_ids.ToArray());
			GenUtils.LogMsg("info", "where_ids: " + where_ids_as_str, null);

			this.what_ids = Metadata.LoadHubIdsFromAzureTableByType(HubType.what);
			var what_ids_as_str = string.Join(",", this.what_ids.ToArray());
			GenUtils.LogMsg("info", "what_ids: " + what_ids_as_str, null);

			this.region_ids = Metadata.LoadHubIdsFromAzureTableByType(HubType.region);
			var region_ids_as_str = string.Join(",", this.region_ids.ToArray());
			GenUtils.LogMsg("info", "region_ids: " + region_ids_as_str, null);

			Dictionary<string, string> ids_and_locations = Metadata.QueryIdsAndLocations();

			this.where_ids.Sort((a, b) => ids_and_locations[a].ToLower().CompareTo(ids_and_locations[b].ToLower()));
			this.what_ids.Sort();
		}

		public static WebRoleData MakeWebRoleData()
		{
			WebRoleData wrd = null;
			try  // create WebRoleData structure and store as blob, available to webrole on next _reload
			{
				var sw = new Stopwatch();
				sw.Start();
				wrd = new WebRoleData();
				sw.Stop();
				GenUtils.LogMsg("info", "new wrd: " + sw.Elapsed.ToString(), null);
				var info = String.Format("new wrd: where_ids: {0}, what_ids: {1}, region_ids {2}", wrd.where_ids.Count, wrd.what_ids.Count, wrd.region_ids.Count);
				GenUtils.LogMsg("info", info, null);
				GenUtils.LogMsg("info", "new wrd: " + wrd.str_ready_ids, null);
				sw.Start();
				if (wrd.IsConsistent())
					SaveWrd(wrd);
				else
				{
					GenUtils.PriorityLogMsg("warning", "MakeWebRoleData: inconsistent", null);
					wrd = GetWrd(); // fall back to last known good
				}
				sw.Stop();
				GenUtils.LogMsg("info", "save wrd: " + sw.Elapsed.ToString(), null);
			}
			catch (Exception e3)
			{
				GenUtils.PriorityLogMsg("exception", "MakeWebRoleData: creating wrd", e3.Message);
			}

			return wrd;
		}

		public static void SaveWrd(WebRoleData wrd)
		{
			if (!wrd.IsConsistent())
			{
				GenUtils.PriorityLogMsg("warning", "inconsistent WebRoleData!", null);
				return;
			}
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var lease_response = bs.RetryAcquireLease("admin", "wrd.obj");
			if (lease_response.status == HttpStatusCode.Created)
			{
				var lease_id = lease_response.headers["x-ms-lease-id"];
				var bytes = ObjectUtils.SerializeObject(wrd);
				var headers = new Hashtable() { { "x-ms-lease-id", lease_id } };
				var r = bs.PutBlob("admin", "wrd.obj", headers, bytes, "binary/octet-stream");
				if (r.HttpResponse.status != HttpStatusCode.Created)
					GenUtils.PriorityLogMsg("warning", "SaveWrd: cannot save", null);

				SaveTimestampedWrd(wrd);
			}
			else
			{
				GenUtils.PriorityLogMsg("warning", "SaveWrd: cannot lease", null);
			}
		}

		public static BlobStorageResponse SaveTimestampedWrd(WebRoleData wrd)
		{
			foreach (var key in wrd.renderers.Keys)
				wrd.renderers[key].cache = null;   // cannot serialize cache
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var timestamped_name = string.Format("wrd." + string.Format("{0:yyyy.MM.dd.HH.mm.ss}.obj", DateTime.UtcNow));
			var bytes = ObjectUtils.SerializeObject(wrd);
			var r = bs.PutBlob("wrd", timestamped_name, bytes, "binary/octet-stream");
			if (r.HttpResponse.status != HttpStatusCode.Created)
				GenUtils.PriorityLogMsg("warning", "SaveTimestampedWrd: cannot save", null);
			return r;
		}

		public static WebRoleData GetWrd()
		{
			return GetWrd("admin", "wrd.obj");
		}

		public static WebRoleData GetWrd(string container, string name)
		{
			var uri = BlobStorage.MakeAzureBlobUri(container, name, false);
			return (WebRoleData)BlobStorage.DeserializeObjectFromUri(uri);
		}

		public void AddNewHubToWrd(string id)
		{
			this.MakeWhereAndWhatAndRegionIdLists();
			var bs = BlobStorage.MakeDefaultBlobStorage();
			try
			{
				if ( this.renderers.ContainsKey(id) == false )
					this.renderers.Add(id, new CalendarRenderer(id));
				if ( this.ready_ids.Exists(x => x == id) == false )
					this.ready_ids.Add(id);
				this.str_ready_ids = String.Join("|", this.ready_ids.ToArray());
				SaveWrd(this);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "AddNewHubToWrd", e.Message);
			}
		}


		public bool IsConsistent()
		{
			var ready_ids_consistent = this.ready_ids.Count == this.where_ids.Count + this.what_ids.Count + this.region_ids.Count;
			var renderers_consistent = this.ready_ids.Count == this.renderers.Count;
			return ready_ids_consistent && renderers_consistent;
		}

	}
}
