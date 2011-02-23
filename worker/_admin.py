import sys, clr

clr.AddReference("System")
clr.AddReference("mscorlib")
import System
from System.IO import Directory

#include common.py

clr.AddReference("CalendarAggregator")
import CalendarAggregator
from CalendarAggregator import *

clr.AddReference("ElmcityUtils")
import ElmcityUtils
from ElmcityUtils import *

import os, traceback

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

def datetime_is_older_than(dt,days):
  now = System.DateTime.Now
  return ( now - dt ).Days > days

def rebuild_search_output():
  message('_admin: rebuild_search_output starting')
  for id in [x for x in ids if calinfos[x].hub_type == CalendarAggregator.HubType.where]:
    try:
      needs_update = False
      r = bs.GetBlobProperties(id, '%s.search.html' % id)
      exists = bs.ExistsBlob(id, id + '.search.html')
      if exists:
        dt = r.HttpResponse.headers['Last-Modified']
        dt = System.DateTime.Parse(dt)
        if ( datetime_is_older_than(dt,7) ):
          needs_update = True
      else:
          needs_update = True
      if needs_update:
        message('_admin: rebuild_search_output: %s' % id)
        Search.SearchLocation(id,calinfos[id].where)
    except:
      message('_admin: rebuild_search_output %s %s ' %s ( id, traceback.format_exc() ) )
  message('_admin: rebuild_search_output done')

def follow_curators():
  message('_admin: follow_curators starting')
  for id in ids:
    try:
      calinfo = Calinfo(id)
      twitterer = calinfo.twitter_account
      if twitterer is not None:
        r = TwitterApi.FollowTwitterAccount(twitterer)
    except:
      message('_admin: follow_curators %s %s ' ( id, traceback.format_exc() ) )
  message('_admin: follow_curators done')

def add_task_for_new_id():
  message('_admin: add_task_for_new_id starting')
  for id in ids:
    try:
      task = Scheduler.FetchTaskForId(id)
    except:
      message('_admin: add_task_for_new_id %s %s ' % (id, tradeback.format_exc() ) )
      Scheduler.InitTaskForId(id)
  message('_admin: add_task_for_new_id done')
       
try:
  add_task_for_new_id()
except:
  message('_admin: error in rebuild_search_output')

try:
  follow_curators()
except:
  message('_admin: error in follow_curators')

try:
  rebuild_search_output()
except:
  message('_admin: error in rebuild_search_output')


