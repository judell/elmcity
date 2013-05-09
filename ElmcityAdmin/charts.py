import clr, sys, re
clr.AddReference('System')
import System
clr.AddReferenceByPartialName('System.Xml')
import System.Xml

clr.AddReference('ElmcityUtils')
import ElmcityUtils
from ElmcityUtils import *

clr.AddReference('CalendarAggregator')
import CalendarAggregator

#include common.py

local_storage = get_local_storage()
python_lib = local_storage + '/Lib'
sys.path.append(python_lib)
import traceback, os   

def make_fname(title, type):
  return 'web_%s_%s.%s' % (System.Net.Dns.GetHostName(), title, type )

def make_html(local_storage, bin, type, in_spec, title, query):
  try:
    xml_fname = make_fname(title, 'xml')
    html_fname = make_fname(title, 'html')
    out_spec = make_out_spec ( local_storage, xml_fname )
    expanded_title = expand_title(title)
    query = query.replace('__IN__', in_spec)
    query = query.replace('__OUT__', out_spec)
    cmd = '%s\\LogParser -q -e:1 -i:"%s" __XML_FNAMES__ -o:XML -oCodepage:"-1" -schemaType:0 "%s" ' % ( bin, type, query)
    if ( type == 'xml' ):
      cmd = cmd.replace('__XML_FNAMES__', '-fnames:xpath')
    else:
      cmd = cmd.replace('__XML_FNAMES__', '')
    GenUtils.LogMsg("info", "make_html", cmd)
    os.system(cmd)
    d = System.Xml.XmlDocument()
    d.Load(out_spec)
    make_html_table(d, expanded_title, out_spec, html_fname )
  except:
    GenUtils.LogMsg("exception", "make_html", format_traceback() )

def get_xml_header_row(row):
  headers = ''
  for field in row.ChildNodes:
    headers += '<td>%s</td>' % field.Name
  return headers

def get_xml_value_row(row):
  vals = ''
  for field in row.ChildNodes:
    val = field.FirstChild.Value
    val = val.Replace('\r','').Replace('\n','').Replace('<','&lt;')
    val = re.sub('[ ]+', ' ', val)
    vals += '<td>%s</td>' % val
  return vals

def make_url(fname):
  return 'http://elmcity.blob.core.windows.net/charts/%s' % fname

def make_html_table(d, title, out_spec, fname):
  try:
    html = """<html>
<head><title>%s</title></head>
<style>
h1 { font-size:smaller }
</style>
<body>
<h1><a href="%s">%s</a></h1>\n<table>
""" % (title, make_url(fname), title )

    html += '<tr>%s</tr>\n' % get_xml_header_row(d.DocumentElement.ChildNodes[0])
    for row in d.DocumentElement.ChildNodes:
      html += '<tr>%s</tr>\n' % get_xml_value_row(row)
    html += '</table>\n</body>\n</html>'
    bs = BlobStorage.MakeDefaultBlobStorage()
    data = System.Text.Encoding.UTF8.GetBytes(html)
    r = bs.PutBlob('charts', fname, System.Collections.Hashtable(), data, 'text/html' )
    print r.HttpResponse.status.ToString()
  except:
    GenUtils.PriorityLogMsg('exception', 'charts.py: make_html_table', format_traceback() )
  
bin = 'e:\\approot\\bin'

GenUtils.LogMsg('info', 'charts.py', repr(get_process_owner()) )

# event queries

title = 'SystemAndApplicationSources'
query = "SELECT SourceName, Count(*) as EventCount into __OUT__ from __IN__ group by SourceName having EventCount > 0 order by EventCount desc"
make_html(local_storage, bin, 'evt', 'System,Application', title, query)

template = "SELECT distinct strcat(SourceName, strcat(': ', substr(Message,0, index_of(Message,'.') )) ) as Key, count(Key) as CountEvents into __OUT__ from __IN__ where EventTypeName like '__EVENTTYPE__%' group by key having CountEvents > 0 order by CountEvents desc"

title = 'ApplicationLogWarnings'
query = template.replace('__EVENTTYPE__', 'Warning')
make_html(local_storage, bin, 'evt', 'Application', title, query)

title = 'ApplicationLogErrors'
query = template.replace('__EVENTTYPE__', 'Error')
make_html(local_storage, bin, 'evt', 'Application', title, query)

title = 'SystemLogWarnings'
query = template.replace('__EVENTTYPE__', 'Warning')
make_html(local_storage, bin, 'evt', 'System', title, query)

title = 'SystemLogErrors'
query = template.replace('__EVENTTYPE__', 'Error')
make_html(local_storage, bin, 'evt', 'System', title, query)

# web queries: tables

try:
  in_spec = '%s/*.log' % get_log_storage()
except:
  GenUtils.PriorityLogMsg('exception', 'charts.py: get_log_storage', format_traceback() )

title = 'Top400Urls'
query = 'select top 50 count(*) as c, cs-uri-stem into __OUT__ from __IN__ where sc-status = 400 group by cs-uri-stem order by c desc'
make_html(local_storage, bin, 'iisw3c', in_spec, title, query)

title = 'TopSlowUrlsStatusEq200'
query = 'select top 50 time-taken as millis, sc-bytes, cs-uri-stem into __OUT__ from __IN__ where sc-status = 200 order by millis desc'
make_html(local_storage, bin, 'iisw3c', in_spec, title, query)

title = 'TopSlowUrlsStatusGt200'
query = 'select top 50 time-taken as millis, sc-status, sc-bytes, cs-uri-stem into __OUT__ from __IN__ where sc-status > 200 order by millis desc'
make_html(local_storage, bin, 'iisw3c', in_spec, title, query)

title = 'TopUrls'
query = 'select top 50 count(*) as c, cs-uri-stem into __OUT__ from __IN__ where sc-status = 200 group by cs-uri-stem order by c desc'
make_html(local_storage, bin, 'iisw3c', in_spec, title, query)

# web queries: charts

title = 'LoadTimesHtmlOnly'
query = "select qntround_to_digit(time-taken, 2) as millis, count(*) as pageloads into __OUT__ from __IN__ where cs-uri-stem like '%html%' group by millis having count(*) > 0 order by millis"
make_chart(local_storage, bin, 'iisw3c', 'ColumnClustered', in_spec, title, query)

title = 'LoadTimes'
query = 'select qntround_to_digit(time-taken, 2) as millis, count(*) as pageloads into __OUT__ from __IN__ group by millis having count(*) > 0 order by millis'
make_chart(local_storage, bin, 'iisw3c', 'ColumnClustered', in_spec, title, query)

title = 'RequestsByStatus'
query = 'select sc-status, count(*) as requests into __OUT__ from __IN__ group by sc-status order by requests desc'
make_chart(local_storage, bin, 'iisw3c', 'ColumnClustered', in_spec, title, query)

title = 'RequestsByHour'
query = 'select quantize(to_timestamp(date,time),3600) as hourly, count(*) as requests into __OUT__ from __IN__ group by hourly order by hourly'
make_chart(local_storage, bin, 'iisw3c', 'ColumnClustered', in_spec, title, query)

title = 'RequestsByIp'
query = 'select top 50 c-ip as dnsname, count(*) as requests into __OUT__ from __IN__ group by dnsname having requests > 0 order by requests desc'
make_chart(local_storage, bin, 'iisw3c', 'ColumnClustered', in_spec, title, query)

# failed requests: tables

try:
  in_spec = '%s/*.xml' % get_failed_request_log_storage()
except:
  GenUtils.PriorityLogMsg('exception', 'charts.py: get_log_storage', format_traceback() )
  
# logparser -q -i:XML -fNames:XPath "select count(/failedRequest/@url), /failedRequest/@url from *.xml where /failedRequest/Event/EventData/Data/@Name = 'RequestURL' group by /failedRequest/@url order by count(/failedRequest/@url) desc" 
title = 'FailedRequestsByCount'
query = "select count(/failedRequest/@url), /failedRequest/@url into __OUT__ from __IN__ where /failedRequest/Event/EventData/Data/@Name = 'RequestURL' group by /failedRequest/@url order by count(/failedRequest/@url) desc"
make_html(local_storage, bin, 'xml', in_spec, title, query)  

title = 'FailedRequestsByStatus'
query = "select /failedRequest/@statusCode, count(/failedRequest/@statusCode) into __OUT__ from __IN__ where /failedRequest/Event/EventData/Data/@Name = 'RequestURL' group by /failedRequest/@statusCode order by count(/failedRequest/@statusCode) desc"
make_html(local_storage, bin, 'xml', in_spec, title, query)  

title = 'FailedRequestTop400s'
query = "select top 10 count(/failedRequest/@url), /failedRequest/@url  into __OUT__ from __IN__ where /failedRequest/Event/EventData/Data/@Name = 'RequestURL' and /failedRequest/@statusCode = '400' group by /failedRequest/@url order by count(/failedRequest/@url) desc"
make_html(local_storage, bin, 'xml', in_spec, title, query) 

title = 'FailedRequestTop404s'
query = "select top 10 count(/failedRequest/@url), /failedRequest/@url into __OUT__ from __IN__ where /failedRequest/Event/EventData/Data/@Name = 'RequestURL' and /failedRequest/@statusCode = '404' group by /failedRequest/@url order by count(/failedRequest/@url) desc"
make_html(local_storage, bin, 'xml', in_spec, title, query) 

title = 'FailedRequestTop200s'
query = "select top 10 count(/failedRequest/@url), /failedRequest/@url into __OUT__ from __IN__ where /failedRequest/Event/EventData/Data/@Name = 'RequestURL' and /failedRequest/@statusCode = '200' group by /failedRequest/@url order by count(/failedRequest/@url) desc"
make_html(local_storage, bin, 'xml', in_spec, title, query) 

title = 'FailedRequestTopOthers'
query = "select top 10 count(/failedRequest/@url), /failedRequest/@statusCode, /failedRequest/@url into __OUT__ from __IN__ where /failedRequest/Event/EventData/Data/@Name = 'RequestURL' and not /failedRequest/@statusCode = '200' and not /failedRequest/@statusCode = '400' and not /failedRequest/@statusCode = '404'group by /failedRequest/@statusCode order by count(/failedRequest/@statusCode) desc"
make_html(local_storage, bin, 'xml', in_spec, title, query) 

try:
  args = System.Collections.Generic.List[str]()
  args.Add('')
  args.Add('')
  args.Add('')
  script_url = CalendarAggregator.Configurator.dashboard_script_url
  PythonUtils.RunIronPython(local_storage, script_url, args)    
except:
  GenUtils.PriorityLogMsg('exception', format_traceback(), None )

# run logparser queries against iis logs and failed request logs
# output charts (gifs) and/or tables (htmls) to charts container in azure storage
# run dashboard to update pages that include charts and tables