import sys, datetime, time, traceback

try:
  IPY = True
  sys.path.append("c:\\users\\jon\\aptc") # for local testing
  import clr

  clr.AddReference("System")
  clr.AddReference("mscorlib")
  import System

  clr.AddReference("CalendarAggregator")
  import CalendarAggregator

  clr.AddReference("ElmcityUtils")
  import ElmcityUtils

  clr.AddReference("DDay.iCal")
  import DDay.iCal
  import DDay.iCal.Components
  import DDay.iCal.DataTypes 
  import DDay.iCal.Serialization 
  print "IPY"

except:
  IPY = False
  print "CPY"

class Event:

  def __init__(self):
    self.title = None
    self.start = None
    self.start_is_utc = False
    self.location = None
    self.url = None

  def __repr__(self):
    title = self.title.encode('utf-8')
    return "<Event: %s, %s, UTC: %s>" % (title, self.start, self.start_is_utc)

class EventParser:

  def __init__(self, url=None, filter=None, tz_source=None, tz_dest=None):
        
    self.url = url
    if ( filter is not None ):
      self.filter = filter.lower()
    else:
      self.filter = filter
    self.tz_source = tz_source
    self.tz_dest = tz_dest
    self.events = []
    self.ics = ''
    self.dtformat = '%Y-%m-%d %H:%M'
    self.month_dict = {'January':1,'February':2,'March':3,'April':4,'May':5,'June':6, 'July':7,'August':8,'September':9,'October':10,'November':11,'December':12}

  if IPY:
    def LogMsg(self,category=None, message=None, details=None):
      #print '%s, %s, %s' % ( category, message, details )
      ElmcityUtils.GenUtils.LogMsg(category, message, details)
  else:
    def LogMsg(self,category=None, message=None, details=None):
      print '%s, %s, %s' % ( category, message, details )

  if IPY:
    def BuildICS(self):
      self.LogMsg("info", "ElmcityEventParser", ','.join(sys.path))
      msg = "BuildICS: called with %s unfiltered events, url: %s, filter: |%s|, tz_source: |%s|" % ( len(self.events), self.url, self.filter, self.tz_source )
      self.LogMsg("info", msg, None)
      try:
        self.ApplyFilter()
        cal = DDay.iCal.iCalendar()
        if ( self.tz_source is not None ):
          tzinfo = CalendarAggregator.Utils.TzinfoFromName(self.tz_source)
          tz = DDay.iCal.Components.iCalTimeZone.FromSystemTimeZone(tzinfo)
          cal.AddChild(tz)
        for event in self.events:
          ical_evt = DDay.iCal.Components.Event(cal)
          ical_evt.Summary = event.title
          dt = event.start
          if ( event.start_is_utc ):
            utc_dtstart = System.DateTime(dt.year,dt.month,dt.day,dt.hour,dt.minute,dt.second, System.DateTimeKind.Utc)
            ical_evt.Start = DDay.iCal.DataTypes.iCalDateTime(utc_dtstart);
          else:        
            ical_evt.Start = DDay.iCal.DataTypes.iCalDateTime(dt.year,dt.month,dt.day,dt.hour,dt.minute,dt.second)
          ical_evt.UID = CalendarAggregator.Event.MakeEventUid(ical_evt)
        serializer = DDay.iCal.Serialization.iCalendarSerializer(cal)
        self.LogMsg("info", "BuildICS: serializing %s filtered events" % len(self.events), None )
        self.ics = serializer.SerializeToString()
      except:
        self.LogMsg('exception', 'BuildICS', traceback.format_exc())

  else:
    def BuildICS(self):
      self.ApplyFilter()
      self.LogMsg("info", "BuildICS: will emit %s filtered events" % len(self.events), None)

  def ParseDateTime(self, date_string, format):
    if IPY:
      import System.DateTime, System.Globalization
      culture = System.Globalization.CultureInfo("en-US")
      fstr = self.StrptimeFormatToDotNetFormat(format)
      dt = System.DateTime.ParseExact(date_string,fstr,culture)
      return datetime.datetime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second)
    else: 
      return datetime.datetime.strptime(date_string, format)

  def StrptimeFormatToDotNetFormat(self, format):
    format = format.replace('%a', 'ddd')
    format = format.replace('%B', 'MMMM')
    format = format.replace('%d', 'd')
    format = format.replace('%I', 'h')
    format = format.replace('%M', 'm')
    format = format.replace('%p', 'tt')
    return format
    
	  
  def ApplyFilter(self):
    
    if self.filter is None:
      return self.events
    
    self.events = [x for x in self.events if x.title and x.title.lower().find(self.filter.lower()) > -1]
