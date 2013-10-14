import clr, sys

clr.AddReference("System")
clr.AddReference("mscorlib")
import System

clr.AddReference("ElmcityUtils")
from ElmcityUtils import *

clr.AddReference("CalendarAggregator")
import CalendarAggregator

#include common.py

local_storage = get_local_storage()
python_lib = local_storage + '/Lib'
sys.path.append(python_lib)
import traceback, os 

hostname = System.Net.Dns.GetHostName()

def make_worker_fname(title, type):
  return 'worker_%s_%s.%s' % (hostname, title, type )

def make_web_fname(title, type):
  return 'worker_%s_%s.%s' % ('ALLWEBROLES', title, type )

def make_xml_blobname():
  return 'worker_%s.xml' % System.Net.Dns.GetHostName()

monitor = '%s/%s' % ( local_storage, 'monitor.xml')

try:
  GenUtils.LogMsg('info', 'worker querying into %s' % monitor, None)
  ts = TableStorage.MakeDefaultTableStorage()
  dt = System.DateTime.UtcNow - System.TimeSpan.FromHours(48)
  filter = "$filter=PartitionKey+eq+'monitor'+and+RowKey+gt+'%s'" % dt.Ticks
  s = ts.QueryEntitiesAsFeed('monitor', filter)
  f = open(monitor, 'w')
  f.write(s)
  f.close()
  GenUtils.LogMsg('info', 'worker saving %s' % monitor, None)
  GenUtils.LogMsg('info', 'worker saving %s' % monitor, None)
  bs.PutBlob('charts', 'monitor.xml', s)
except:
  print format_traceback()
  GenUtils.PriorityLogMsg('exception', 'MakeChart', format_traceback() )

# charts

bin = "e:\\approot"

in_spec = '%s#/feed/entry/content' % monitor

# web queries (worker doesn't know web hostnames so these include all webroles)

make_fname = make_web_fname

template = "select to_timestamp(d:TimeStamp, 'yyyy-MM-ddThh:mm:ss.???????Z') as when, %s into __OUT__ from __IN__ where d:ProcName = '%s' order by when"

fields = 'd:asp_reqs_current, d:ThreadCount'

title = 'WebCurrentRequestsAndThreads'
query = template % ( fields, 'w3wp' )
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:processor_pct_proctime, d:ThreadCount'

title = 'WebProcessorAndThreads'
query = template % ( fields, 'w3wp' )
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:mem_available_mbytes'

title = 'WebMemAvailable'
query = template % ( fields, 'w3wp' )
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

title = 'WorkerMemAvailable'
query = template % ( fields, 'WaWorkerHost')
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:asp_net_reqs_per_sec'

title = 'AspNetReqsPerSec'
query = template % ( fields, 'w3wp')
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:asp_reqs_current'
title = 'AspReqsCurrent'
query = template % ( fields, 'w3wp')
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:asp_net_reqs_per_sec'
title = 'AspNetReqsPerSec'
query = template % ( fields, 'w3wp')
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:cache_api_hit_ratio, d:output_cache_hit_ratio'
title = 'CacheRatios'
query = template % ( fields, 'w3wp')
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

template = "select to_timestamp(d:TimeStamp, 'yyyy-MM-ddThh:mm:ss.???????Z') as when, %s into __OUT__ from __IN__ where d:ProcName = '%s' and d:asp_req_exec_time < %s order by when"

fields = 'd:asp_req_exec_time'

title = 'AspNetExecTimeAll'
query = template % ( fields, 'w3wp', 99999999)
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

title = 'AspNetExecTimeLt100Secs'
query = template % ( fields, 'w3wp', 100000)
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

title = 'AspNetExecTimeLt10Secs'
query = template % ( fields, 'w3wp', 10000)
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

title = 'AspNetExecTimeLt1Sec'
query = template % ( fields, 'w3wp', 1000)
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:asp_net_reqs_succeeded, d:asp_net_reqs_failed'

title = 'AspNetReqsSucceededAndFailed'
query = template % ( fields, 'w3wp', 1000)
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

# worker queries

make_fname = make_worker_fname

template = "select to_timestamp(d:TimeStamp, 'yyyy-MM-ddThh:mm:ss.???????Z') as when, %s into __OUT__ from __IN__ where d:HostName = '%s' and d:ProcName = '%s' order by when"

fields = 'd:mem_available_mbytes'

title = 'WorkerMemAvailable'
query = template % ( fields, hostname, 'WaWorkerHost')
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

fields = 'd:processor_pct_proctime, d:ThreadCount'

title = 'WorkerProcessorAndThreads'
query = template % ( fields, hostname, 'WaWorkerHost')
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)


try:
  args = System.Collections.Generic.List[str]()
  args.Add('')
  args.Add('')
  args.Add('')
  script_url = CalendarAggregator.Configurator.dashboard_script_url
  PythonUtils.RunIronPython(local_storage, script_url, args)    
except:
  GenUtils.PriorityLogMsg('exception', format_traceback(), None )
  
GenUtils.LogMsg("info", "monitor.py stopping", None)

# pull 24 hours of diagnostics from odata feed into a file
# run logparser queries against the file
# output charts (gifs) and/or tables (htmls) to charts container in azure storage
# run dashboard to update pages that include charts and tables



