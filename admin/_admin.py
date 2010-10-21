import sys, clr

sys.path.append("c:\\users\\jon\\aptc")
clr.AddReference("System")
clr.AddReference("mscorlib")
import System

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

def delete_dict(dict):
  pk = dict['PartitionKey']
  rk = dict['RowKey']
  tsr = ts.DeleteEntity(metatable,pk,rk)

def snapshot_feeds_and_metadata(id):
  message('_admin: snapshot_feeds_and_metadata: %s' % id)
  s = ''
  q = "$filter=(PartitionKey eq '%s' and feedurl ne '' )" % id 
  ts_response = ts.QueryEntities(metatable,q)
  for dict in ts_response.response:
    s += message('source %s, feedurl %s\n' % ( dict['source'], dict['feedurl'] ) )
#    delete_dict(dict)
  now = System.DateTime.UtcNow.ToString("yyyyMMdd-HHmm")
  filename = 'feeds_and_metadata_' + now + '.txt'
  bs.PutBlob(id, filename, System.Collections.Hashtable(), System.Text.Encoding.UTF8.GetBytes(s), "text/plain")

def snapshot_feeds_and_metadata_for_ids():
  message('_admin: snapshot_feeds_and_metadata_for_ids start')
  for id in ids:
    snapshot_feeds_and_metadata(id)

"""
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

  dump_metadata_result += "# feeds for ids\n\n"
  for id in ids:
#    message('_admin: dump_metadata feed: %s' % id)
    dump_metadata_result += "%s\n\n" % id
    q = "$filter=(PartitionKey eq '%s' and feedurl ne '' )" % ( id )
    r = ts.QueryEntities(metatable,q)
    dump_metadata_result += unpack(r)
  dump_metadata_result += '\n\n'

  dump_metadata_result += "# venues for ids\n\n"
  for id in ids:
#    message('_admin: dump_metadata venue: %s' % id)
    dump_metadata_result += "%s\n\n" % id
    q = "$filter=(PartitionKey eq '%s_venues')" % ( id )
    r = ts.QueryEntities(metatable,q)
    dump_metadata_result += unpack(r)
  dump_metadata_result += '\n\n'

  bs.PutBlob("admin", "dump_metadata.txt", System.Collections.Hashtable(), System.Text.Encoding.UTF8.GetBytes(dump_metadata_result), "text/plain")

  message('_admin: dump_metadata stop')

def list_blobs():
  message('_admin: eccblobs start')
  list_blobs_result = ''
  for id in ids:
#    message('_admin: list_blobs: %s' % id)
    list_blobs_result += "\n%s\n" % id
    r = bs.ListBlobs(id)
    for dict in r.response:
      list_blobs_result += "\t%s %s\n" % ( dict["LastModified"], dict["Name"] )
    bs.PutBlob("admin", "list_blobs.txt", System.Collections.Hashtable(), System.Text.Encoding.UTF8.GetBytes(list_blobs_result), "text/plain")
  message('_admin: eccblobs stop')

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
  dump_metadata() # -> dump_metadata.txt
except:
  message('_admin: error in dump_metadata')

try:
  list_blobs()    # -> list_blobs.txt
except:
  message('_admin: error in list_blobs')

try:
  rebuild_search_output()
except:
  message('_admin: error in rebuild_search_output')

try:
  snapshot_feeds_and_metadata_for_ids()
except:
  message('_admin: error in snapshot_feeds_and_metadata_for_ids')


