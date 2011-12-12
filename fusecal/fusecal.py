import sys, os, traceback

import clr

clr.AddReference("ElmcityUtils")
import ElmcityUtils

clr.AddReference("System")
clr.AddReference("mscorlib")
import System

logger = ElmcityUtils.Logger()

global result

def Dispatch(url=None,filter=None,tz_source=None,tz_dest=None):
 
  if ( filter is "" ):
    filter = None

  if ( tz_source is "" ):
    tz_source = None

  if ( tz_dest is "" ):
    tz_dest = None

  if url.lower().find('myspace.com') != -1:
    from myspace import MySpaceParser
    parser = MySpaceParser(url=url, filter=filter, tz_source=tz_source)

  if url.lower().find('libraryinsight.com') != -1:
    from libraryinsight import LibraryInsightParser
    parser = LibraryInsightParser(url=url, filter=filter, tz_source=tz_source)

  if url.lower().find('librarything.com') != -1:
    from librarything import LibraryThingParser
    parser = LibraryThingParser(url=url, filter=filter, tz_source=tz_source)

  if parser is not None:
    parser.Parse()
    return parser.ics

  return ""

args = 'args: (%s) ' % ','.join(sys.argv)

logger.LogMsg("info", '(fusecal.py)', ','.join(sys.path))

try: 
  url = sys.argv[0]  
  filter = sys.argv[1]
  tz_source = sys.argv[2]
  logger.LogMsg("info", '(fusecal) url %s, filter %s, tz_source %s' % ( url, filter, tz_source), None)
  result = Dispatch(url=url, filter=filter, tz_source=tz_source)
except:
  logger.LogMsg("exception", traceback.format_exc(), None)

 

