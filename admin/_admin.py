import sys, clr

clr.AddReference("System")
clr.AddReference("mscorlib")
import System
from System.IO import Directory

resource_dirs = Directory.GetDirectories('c:\\Resources\\Directory')
lib_dir = [dir for dir in resource_dirs if dir.endswith('LocalStorage1')][0]
sys.path = []
sys.path.append(lib_dir + "\Lib")
sys.path.append(lib_dir + "\Lib\site-packages")
sys.path.append(lib_dir + "\ElmcityLib")

import os, traceback

clr.AddReference("CalendarAggregator")
import CalendarAggregator
from CalendarAggregator import *

clr.AddReference("ElmcityUtils")
import ElmcityUtils
from ElmcityUtils import *

metatable = 'metadata'
tasktable = 'tasks'

delicious = Delicious.MakeDefaultDelicious()
bs = BlobStorage.MakeDefaultBlobStorage()
ts = TableStorage.MakeDefaultTableStorage()
ids = delicious.LoadHubIdsFromAzureTable()
calinfos = CalendarAggregator.Configurator.Calinfos

def message(msg):
  msg = msg.replace('\n','')
  GenUtils.LogMsg(msg, '', '')
  return msg

"""
def delete_dict(dict):
  pk = dict['PartitionKey']
  rk = dict['RowKey']
  tsr = ts.DeleteEntity(metatable,pk,rk)

  # delete venues from aztable
  q = "$filter=(PartitionKey eq '%s_venues')" % ( id )
  ts_response = ts.QueryEntities(metatable,q)
  for dict in ts_response.response:
    message('venue_url %s' % dict['venue_url'])
    delete_dict(dict)
"""

def unpack(ts_response):
  s = ''
  for dict in ts_response.response:
    for key in dict.Keys:
      s += '%s: %s\n' % ( key, dict[key] )
    s += '\n'
  return s

def dump_metadata():
  message('_admin: dump_metadata start')
  dump_metadata_result = '' 
  dump_metadata_result += "# master list of ids\n\n"
  q = "$filter=(PartitionKey eq '%s' and RowKey eq '%s' )" % ( "master", "accounts" )
  r = ts.QueryEntities(metatable,q)
  dump_metadata_result += unpack(r)
  dump_metadata_result += '\n\n'

  dump_metadata_result += "# metadata for ids\n\n"
  for id in ids:
#    message('_admin: dump_metadata metadata: %s' % id)
    dump_metadata_result += "%s\n\n" % id
    q = "$filter=(PartitionKey eq '%s' and RowKey eq '%s' )" % ( id, id )
    r = ts.QueryEntities(metatable,q)
    dump_metadata_result += unpack(r)
  dump_metadata_result += '\n\n'

  dump_metadata_result += "# tasks for ids\n\n"
  for id in ids:
#    message('_admin: dump_metadata task: %s' % id)
    dump_metadata_result += "%s\n\n" % id
    q = "$filter=(PartitionKey eq 'master' and RowKey eq '%s' )" % ( id )
    r = ts.QueryEntities(tasktable,q)
    dump_metadata_result += unpack(r)
  dump_metadata_result += '\n\n'

  dump_metadata_result += "# locks for ids\n\n"
  for id in ids:
#    message('_admin: dump_metadata lock: %s' % id)
    dump_metadata_result += "%s\n\n" % id
    q = "$filter=(PartitionKey eq 'lock' and RowKey eq '%s' )" % ( id )
    r = ts.QueryEntities(tasktable,q)
    dump_metadata_result += unpack(r)
  dump_metadata_result += '\n\n'

  bs.PutBlob("admin", "dump_metadata.txt", System.Collections.Hashtable(), System.Text.Encoding.UTF8.GetBytes(dump_metadata_result), "text/plain")

  message('_admin: dump_metadata stop')


def datetime_is_older_than(dt,days):
  now = System.DateTime.Now
  return ( now - dt ).Days > days

def rebuild_search_output():
  message('_admin: rebuild_search_output start')
  for id in [x for x in ids if calinfos[x].hub_type == 'where']:
    needs_update = False
    r = bs.GetBlobProperties(id, '%s.search.html' % id)
    exists = bs.ExistsBlob(id, id + '.search.html')
    if exists:
      dt = r.http_response.headers['Last-Modified']
      dt = System.DateTime.Parse(dt)
      if ( datetime_is_older_than(dt,7) ):
        needs_update = True
    else:
        needs_update = True
    if needs_update:
      message('_admin: rebuild_search_output: %s' % id)
      Search.SearchLocation(id,calinfos[id].where)
  message('_admin: rebuild_search_output stop')

def follow_curators():
  for id in ids:
    print "follow_curators: " + id
    calinfo = Calinfo(id)
    twitterer = calinfo.twitter_account
    if twitterer is not None:
     r = TwitterApi.FollowTwitterAccount(twitterer)

try:
  follow_curators()
except:
  message('_admin: error in follow_curators')

try:
  rebuild_search_output()
except:
  message('_admin: error in rebuild_search_output')

