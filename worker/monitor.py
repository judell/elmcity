import clr, sys

clr.AddReference("System")
clr.AddReference("mscorlib")
import System

clr.AddReference("ElmcityUtils")
from ElmcityUtils import *

clr.AddReference('System.Management')
import System.Management

#include common.py

def make_fname(title, type):
  return 'worker_%s_%s.%s' % (System.Net.Dns.GetHostName(), title, type )

def make_out_spec(local_storage, fname):
  return '%s/%s' % ( local_storage, fname )      

def expand_title( title ):
  return '%s - %s - %s' % ( title, System.DateTime.UtcNow.ToString(), System.Net.Dns.GetHostName() )

local_storage = get_local_storage()
  
uri = System.Uri('http://elmcity.blob.core.windows.net/admin/python_library.zip')
FileUtils.UnzipFromUrlToDirectory(uri, local_storage)  

sys.path.append( '%s\Lib' % local_storage )
import os, traceback

GenUtils.LogMsg('info', 'monitor.py', repr(get_process_owner()) )

def make_chart(local_storage, bin, source_type, chart_type, in_spec, title, query):
  try:
    GenUtils.LogMsg("info", "query: " + query, None)    
    fname = make_fname ( title, 'gif' )
    out_spec = make_out_spec( local_storage, fname )
    expanded_title = expand_title ( title )
    query = query.replace('__IN__', in_spec)
    query = query.replace('__OUT__', out_spec)
    cmd = '%s\\LogParser -q -e:1 -i:%s -o:CHART -categories:ON -groupSize:1500x800 -legend:ON -ChartTitle:"%s" -chartType:"%s" "%s"' % ( bin, source_type, expanded_title, chart_type, query )
    GenUtils.LogMsg("info", "make_chart: " + cmd, None)
    os.system(cmd)
    bs = BlobStorage.MakeDefaultBlobStorage()
    data = System.IO.File.ReadAllBytes(out_spec)
    bs.PutBlob('charts', fname, System.Collections.Hashtable(), data, "image/gif" )
  except:
    GenUtils.LogMsg('exception', 'MakeChart: ' + title, format_traceback() )

monitor = '%s/%s' % ( local_storage, 'monitor.xml')

try:
  GenUtils.LogMsg('info', 'worker querying into %s' % monitor, None)
  ts = TableStorage.MakeDefaultTableStorage()
  dt = System.DateTime.UtcNow - System.TimeSpan.FromHours(24)
  filter = "$filter=PartitionKey+eq+'monitor'+and+RowKey+gt+'%s'" % dt.Ticks
  s = ts.QueryEntitiesAsFeed('monitor', filter)
  f = open(monitor, 'w')
  f.write(s)
  f.close()
  GenUtils.LogMsg('info', 'worker saving %s' % monitor, None)
except:
  GenUtils.LogMsg('exception', 'MakeChart', format_traceback() )

# charts

bin = "e:\\approot"

in_spec = '%s#/feed/entry/content' % monitor

#template = "select to_string(to_timestamp(d:TimeStamp, 'yyyy-MM-ddThh:mm:ss.???????Z'), 'dd hh:mm') as when, %s into __OUT__ from __IN__ where d:ProcName = '%s' order by when"

template = "select to_timestamp(d:TimeStamp, 'yyyy-MM-ddThh:mm:ss.???????Z') as when, %s into __OUT__ from __IN__ where d:ProcName = '%s' order by when"

fields = 'd:processor_pct_proctime, d:ThreadCount'

title = 'WebProcessorAndThreads'
query = template % ( fields, 'w3wp' )
make_chart(local_storage, bin, 'xml', 'Line', in_spec, title, query)

title = 'WorkerProcessorAndThreads'
query = template % ( fields, 'WaWorkerHost')
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

try:
  args = System.Collections.Generic.List[str]()
  args.Add('')
  args.Add('')
  args.Add('')
  script_url = CalendarAggregator.Configurator.dashboard_script_url
  PythonUtils.RunIronPython(local_storage, script_url, args)    
except:
  GenUtils.LogMsg('exception', format_traceback(), None )
  
GenUtils.LogMsg("info", "monitor.py stopping", None)

