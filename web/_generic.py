import sys, clr

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
delicious = Delicious.MakeDefaultDelicious()
ids = delicious.LoadHubIdsFromAzureTable()

logtable = 'log'
metatable = 'metadata'
tasktable = 'tasks'

(arg0,arg1,arg2) = ( sys.argv[0], sys.argv[1], sys.argv[2] )

usage = """
-----------------------------
Usage:

/test
/messages/TYPE/HOURS_AGO
/schedule/ID
/reset/ID
/metadict/ID
/feeds/ID
/recache_feeds/ID
/recache_metadict/ID
/web_charts
/dashboard
-----------------------------
"""

result = "(%s) arg0: [%s], arg1: [%s] arg2: [%s] [%s]\n %s \n\n" % (System.Net.Dns.GetHostName(), arg0,arg1,arg2, lib_dir, usage)

def MessagesSince(type='exception',hours_ago=1,ticks=None):
  global result
  result += 'MessagesSince: Type %s, HoursAgo %s, TicksThen %s\n' % ( type, hours_ago, ticks)
  if ticks is None:
    delta = System.TimeSpan.FromHours(System.Convert.ToInt32(hours_ago))
    then = System.DateTime.Now - delta
    result += 'Then: %s\n\n' % then
    ticks = then.Ticks
  q = "$filter=(PartitionKey eq 'log' and RowKey gt '%s' and type eq '%s' )" % ( ticks, type )
  result += q
  ts_response = ts.QueryEntities(logtable,q)
  if len(ts_response.response) > 0:
    for dict in ts_response.response:
      result += '%s: %s\n%s\n\n'  % ( dict['Timestamp'], dict['message'], dict['data'] ) 
    MessagesSince(type, hours_ago, dict['RowKey'])

def message(msg):
  print msg
  ts.WriteLogMessage(msg, "", None)
  return msg

def get_task(task,calinfo):
  id = calinfo.delicious_account
  interval = System.TimeSpan(CalendarAggregator.Configurator.where_aggregate_interval_hours, 0, 0);
  s = """%s
    start: %s
     stop: %s
 runnning: %s
abandoned: %s
   locked: %s

""" % ( task.id, 
        task.start.ToString(), 
        task.stop.ToString(), 
        task.running.ToString(),
        Scheduler.IsAbandoned(id,interval).ToString(),
        Scheduler.IsLockedId(id).ToString()
       )
  return s

def unlock_and_init(id):
   Scheduler.UnlockId(id)
   Scheduler.InitTaskForId(id)

def get_task_for_calinfo(calinfo):
  task = Scheduler.FetchTaskForId(calinfo.delicious_account)
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


    result += "Delicious.FetchFeedMetadataFromDeliciousForFeedurlAndId('http://openmikes.org/listings/darkstarpub?ical', 'elmcity')" + '\n\n'
    r = Delicious.FetchFeedMetadataFromDeliciousForFeedurlAndId('http://openmikes.org/listings/darkstarpub?ical', 'elmcity')
    result += r.http_response.DataAsString()
    result += '\n\n'

    result += "webrole_reload_interval_hours: %s\n\n" % CalendarAggregator.Configurator.webrole_reload_interval_hours

  except:
    result += traceback.format_exc()

if (arg0 == 'messages'):
  MessagesSince(type=arg1,hours_ago=arg2)

if (arg0 == 'schedule'):
  id = arg1
  calinfo = Calinfo(id)
  result += get_task_for_calinfo(calinfo)

if ( arg0 == 'reset' ):
  id = arg1
  Scheduler.InitTaskForId(arg1)
  result += 'ok'

if ( arg0 == 'metadict' ):
  id = arg1
  calinfo = Calinfo(id)
  metadict = calinfo.metadict
  for key in metadict.Keys:
    result += "%s: %s\n" % (key, metadict[key])

if ( arg0 == 'feeds' ):
  id = arg1
  calinfo = Calinfo(id)
  fr = FeedRegistry(id)
  fr.LoadFeedsFromAzure()
  for key in fr.feeds.Keys:
    result += "%s: %s\n" % (fr.feeds[key], key)

if ( arg0 == 'recache_feeds' ):
  id = arg1
  # delete feedurls from aztable
  q = "$filter=(PartitionKey eq '%s' and feedurl ne '' )" % id 
  result += q
  ts_response = ts.QueryEntities(metatable,q)
  for dict in ts_response.response:
    result += message('purging: source %s, feedurl %s\n' % ( dict['source'], dict['feedurl'] ) )
    delete_dict(dict)
  # recache feedurls to aztable
  fr = FeedRegistry(id)
  fr.LoadFeedsFromDelicious()
  result += 'recaching feeds...'
  delicious.StoreMetadataForFeedsToAzure(id,fr)
  result += '...done recaching'

if ( arg0 == 'recache_metadict' ):
  id = arg1
  result += 'recaching metadict...'
  dict = System.Collections.Generic.Dictionary[str,str]()
  delicious.StoreMetadataForIdToAzure(id,True,dict)
  result += '...done recaching'

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




