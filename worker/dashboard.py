import clr, sys, re

clr.AddReference('System')
import System

clr.AddReference('ElmcityUtils')
from ElmcityUtils import *

#include common.py
  
local_storage = get_local_storage()
python_lib = local_storage + '/Lib'
sys.path.append(python_lib)
import traceback   
  
bs = BlobStorage.MakeDefaultBlobStorage()
r = bs.ListBlobs('charts').response

settings = GenUtils.GetSettingsFromAzureTable()
web_interval = settings['web_make_tables_and_charts_interval_minutes']
worker_interval = settings['worker_gather_monitor_data_interval_minutes']

def blob_time_to_dt(blob):
  lm = blob['Last-Modified']
  return System.DateTime.Parse(lm)

def is_recent_blob(blob, interval):
  now = System.DateTime.UtcNow
  last_mod = blob_time_to_dt(blob)
  diff = now - last_mod
  return diff <= System.TimeSpan.FromMinutes(int(interval))

def is_recent_web_blob(blob):
  return  blob['Name'].startswith('web') and is_recent_blob(blob, web_interval)

def is_recent_worker_blob(blob):
  return  blob['Name'].startswith('worker') and is_recent_blob(blob, worker_interval)

def is_html_blob(blob):
  return blob['Name'].endswith('html')

def is_gif_blob(blob):
  return  blob['Name'].endswith('gif')

def hostname(blob):
  return re.findall('_([^_]+)', blob['Name'])[0]

def title(blob):
  return re.findall('_.+_([^\.]+)', blob['Name'])[0]

recent_worker_blobs = [blob for blob in r if is_recent_worker_blob(blob)]
recent_web_blobs = [blob for blob in r if is_recent_web_blob(blob)]

recent_worker_gifs = [blob for blob in recent_worker_blobs if is_gif_blob(blob)]
recent_worker_htmls = [blob for blob in recent_worker_blobs if is_html_blob(blob)]

recent_web_gifs = [blob for blob in recent_web_blobs if is_gif_blob(blob)]
recent_web_htmls = [blob for blob in recent_web_blobs if is_html_blob(blob)]

recent_web_hosts = list(set([hostname(blob) for blob in recent_web_blobs]))
recent_web_gif_titles = list(set([title(blob) for blob in recent_web_gifs]))
recent_web_html_titles = list(set([title(blob) for blob in recent_web_htmls]))
recent_worker_gif_titles = list(set([title(blob) for blob in recent_worker_gifs]))

recent_gifs = recent_worker_gifs + recent_web_gifs
recent_gifs.sort(lambda x,y: cmp(title(x)+hostname(x), title(y)+hostname(y)))

recent_htmls = recent_web_htmls + recent_worker_htmls
recent_htmls.sort(lambda x,y: cmp(title(x)+hostname(x), title(y)+hostname(y)))

def make_html_page(name, blobs, pctwide, high):
  html = """<html><head><title>%s</title><body>__BODY__</body></html>""" % name
  s = ''
  for blob in blobs:
    url = blob['Url']
    s += '<iframe style="width:%s%%;height:%spx" src="%s"></iframe>\n' % (pctwide, high, url)
  html = html.replace('__BODY__',s)
  name = name + '.html'
  bs.PutBlob('charts', name, html, "text/html")

def make_gif_page(name, blobs, pct):
  html = ''
  for blob in blobs:
    url = blob['Url']
    html += '<a href="%s"><img style="border-style:solid;border-color:black;border-width:thin;width:%s%%" src="%s"></a>\n' % (url, pct, url)
  name = name + '.html'
  bs.PutBlob('charts', name, html, 'text/html')

try:
  GenUtils.LogMsg('info', 'dashboard.py make_html', None)
  make_html_page('recent_htmls', recent_htmls, 49, 300)
  GenUtils.LogMsg('info', 'dashboard.py make_gif', None)
  make_gif_page('recent_gifs', recent_gifs, 49)
except:
  GenUtils.PriorityLogMsg('exception', 'dashboard.py', format_traceback() )
  