import sys, clr, re

clr.AddReference("System")
clr.AddReference("mscorlib")
import System

#include common.py
lib_dir = get_local_storage()

from System.IO import Directory

import os, traceback

clr.AddReference("CalendarAggregator")
import CalendarAggregator
from CalendarAggregator import *

clr.AddReference("ElmcityUtils")
import ElmcityUtils
from ElmcityUtils import *

ts = TableStorage.MakeDefaultTableStorage()
ids = Metadata.LoadHubIdsFromAzureTable()

logtable = 'log'
metatable = 'metadata'
tasktable = 'tasks'

(arg0,arg1,arg2) = ( sys.argv[0], sys.argv[1], sys.argv[2] )

usage = """
-----------------------------
Usage:

/test
/get_fb_ical_url
/schedule/ID
/reset/ID
/feeds/ID
/web_charts
/dashboard
/repickle/ID
-----------------------------
"""

result = "(%s) arg0: [%s], arg1: [%s] arg2: [%s] [%s]\n %s \n\n" % (System.Net.Dns.GetHostName(), arg0,arg1,arg2, lib_dir, usage)

def message(msg):
  print msg
  ts.WriteLogMessage(msg, "", None)
  return msg

def get_task(task,calinfo):
  id = calinfo.id
  interval = System.TimeSpan(CalendarAggregator.Configurator.where_aggregate_interval_hours, 0, 0);
  s = """%s
    start: %s
     stop: %s
   status: %s
abandoned: %s
   locked: %s

""" % ( task.id, 
        task.start.ToString(), 
        task.stop.ToString(), 
        task.status.ToString(),
        Scheduler.IsAbandoned(id,interval).ToString(),
        Scheduler.IsLockedId(id).ToString()
       )
  return s

def get_task_for_calinfo(calinfo):
  task = Scheduler.FetchTaskForId(calinfo.id)
  return get_task(task,calinfo)

def delete_dict(dict):
  pk = dict['PartitionKey']
  rk = dict['RowKey']
  tsr = ts.DeleteEntity(metatable,pk,rk)


# main

if (arg0 == 'test'):
  try:

    import sys, os, glob

    import time

    result += System.Diagnostics.Process.GetCurrentProcess().ProcessName + ", " + System.Diagnostics.Process.GetCurrentProcess().StartInfo.UserName;'\n\n'

    result += 'path: ' + ', '.join(sys.path) + '\n\n'

    result += 'cwd: ' + os.getcwd() + '\n\n'
    result += 'contents of cwd: ' + ', '.join(glob.glob('./*')) + '\n\n'

    os.chdir('e:/approot/bin')

    result += 'cwd: ' + os.getcwd() + '\n\n'
    result += 'contents of cwd: ' + ', '.join(glob.glob('./*')) + '\n\n'

    result += "facebook_mystery_offset_hours: %s\n\n" % CalendarAggregator.Configurator.facebook_mystery_offset_hours

  except:
    result += traceback.format_exc()

if (arg0 == 'schedule'):
  id = arg1
  calinfo = Calinfo(id)
  result += get_task_for_calinfo(calinfo)

if ( arg0 == 'reset' ):
  id = arg1
  Scheduler.InitTaskForId(arg1,TaskType.icaltasks)
  result += 'ok'

if ( arg0 == 'feeds' ):
  id = arg1
  calinfo = Calinfo(id)
  fr = FeedRegistry(id)
  fr.LoadFeedsFromAzure(FeedLoadOption.all)
  for key in fr.feeds.Keys:
    result += "%s: %s\n" % (fr.feeds[key], key)

if ( arg0 == 'web_charts' ):
  result += 'starting web charts...'
  args = System.Collections.Generic.List[str]()
  args.Add('')
  args.Add('')
  args.Add('')
  PythonUtils.RunIronPython(lib_dir, CalendarAggregator.Configurator.charts_and_tables_script_url, args)
  result += '...stopping web charts'

if ( arg0 == 'dashboard' ):
  try:
    result += 'starting dashboard...'
    args = System.Collections.Generic.List[str]()
    args.Add('')
    args.Add('')
    args.Add('')
    script_url = CalendarAggregator.Configurator.dashboard_script_url
    PythonUtils.RunIronPython(lib_dir, script_url, args)
    result += 'stopping dashboard...'
  except:
    tb = format_trace_back()
    result += tb
    GenUtils.PriorityLogMsg('info', '_run.py', tb)

if ( arg0 == 'pylib' ):
  result = os.path.realpath('.')

if ( arg0 == 'repickle' ):
  id = arg1    
  CalendarAggregator.Utils.RecreatePickledCalinfoAndRenderer(id); 

if ( arg0 == 'get_fb_ical_url' ):
  try:
    fb_page_url = arg1
    elmcity_id = arg2
#    fb_page = ElmcityUtils.HttpUtils.FetchUrl(fb_page_url).DataAsString()
#    fb_id = re.findall('\?id="*(\d+)',fb_page)[0]
#    result = 'http://elmcity.cloudapp.net/ics_from_fb_page?fb_id=%s&elmcity_id=%s' % ( fb_id, elmcity_id ) 
    result = fb_page_url
  except:
    tb = format_trace_back()
    result = tb
    GenUtils.PriorityLogMsg('info', 'get_fb_ical_url', tb)
  
 




