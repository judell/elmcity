import sys, os, traceback

#sys.path.append("c:\\users\\jon\\aptc")
#sys.path.append("c:\\program files\\ironpython 2.6\\lib")
import clr

clr.AddReference("CalendarAggregator")
import CalendarAggregator
from CalendarAggregator import *

clr.AddReference("ElmcityUtils")
import ElmcityUtils
from ElmcityUtils import *

clr.AddReference("System")
clr.AddReference("mscorlib")
import System

ts = TableStorage.MakeDefaultTableStorage()
delicious = Delicious.MakeDefaultDelicious()
ids = delicious.LoadHubIdsFromAzureTable()

logtable = 'log'
metatable = 'metadata'
tasktable = 'tasks'

(arg0,arg1,arg2) = ( sys.argv[0], sys.argv[1], sys.argv[2] )

result = "arg0: %s, arg1: %s arg2: %s\n\n" % (arg0,arg1,arg2)

def MessagesSince(type='exception',days_ago=1,ticks=None):
  global result
  result += 'MessagesSince: Type %s, DaysAgo %s, TicksThen %s\n' % ( type, days_ago, ticks)
  if ticks is None:
    delta = System.TimeSpan.FromDays(System.Convert.ToInt32(days_ago))
    then = System.DateTime.Now - delta
    result += 'Then: %s\n\n' % then
    ticks = then.Ticks
  q = "$filter=(PartitionKey eq 'log' and RowKey gt '%s' and type eq '%s' )" % ( ticks, type )
  result += q
  ts_response = ts.QueryEntities(logtable,q)
  if len(ts_response.response) > 0:
    for dict in ts_response.response:
      result += '%s: %s\n%s\n\n'  % ( dict['Timestamp'], dict['message'], dict['data'] ) 
    MessagesSince(type, days_ago, dict['RowKey'])

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

    result += 'path: ' + ', '.join(sys.path) + '\n\n'

    result += 'cwd: ' + os.getcwd() + '\n\n'

    result += 'contents of cwd: ' + ', '.join(glob.glob('./*')) + '\n\n'


    result += "Delicious.FetchFeedMetadataFromDeliciousForFeedurlAndId('http://openmikes.org/listings/darkstarpub?ical', 'elmcity')" + '\n\n'
    r = Delicious.FetchFeedMetadataFromDeliciousForFeedurlAndId('http://openmikes.org/listings/darkstarpub?ical', 'elmcity')
    result += r.http_response.DataAsString()
    result += '\n\n'

    result += "webrole_reload_interval_hours: %s\n\n" % CalendarAggregator.Configurator.webrole_reload_interval_hours

#    result += 'contents of ElmcityLib/myspace.py: \n'
#    result += open('./ElmcityLib/myspace.py').read()

#    result += 'contents of ElmcityLib/libraryinsight.py: \n'
#    result += open('./ElmcityLib/libraryinsight.py').read()

#    result += 'contents of ElmcityLib/ElmcityEventParser.py: \n'
#    result += open('./ElmcityLib/ElmcityEventParser.py').read()

#    result += "reinstalling ElmcityLib"
#    PythonUtils.InstallPythonElmcityLibrary(ts)

#    args = System.Collections.Generic.List[str]()
#    args.Add('http://www.libraryinsight.com/calendar.asp?jx=ea')
#    args.Add('')
#    args.Add('eastern')
#    result += PythonUtils.RunIronPython(CalendarAggregator.Configurator.fusecal_dispatcher, args);
#    PythonUtils.RunIronPython(CalendarAggregator.Configurator.fusecal_dispatcher, args);
  except:
    result += traceback.format_exc()

if (arg0 == 'messages'):
  MessagesSince(type=arg1,days_ago=arg2)

if (arg0 == 'schedule'):
  for id in ids:
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

if ( arg0 == 'pylib' ):
  result = os.path.realpath('.')


