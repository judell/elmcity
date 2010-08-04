import ElmcityEventParser

import sys, re, urllib2, traceback, icalendar, traceback, datetime

class LibraryInsightParser(ElmcityEventParser.EventParser):

  def Parse(self):

    try:
      self.LogMsg("info", "LibraryInsightParser.Parse", ','.join(sys.path))
      self.ParsePage()
    except:
      self.LogMsg('exception', 'LibraryInsightParser.Parse', traceback.format_exc())

    self.BuildICS()

  def ParsePage(self):

     html = urllib2.urlopen(self.url).read()
     ids = re.findall('lmx=(\d+)',html)  # 287809

     uniques = {}

     for id in ids:

       ical_url = 'http://www.libraryinsight.com/tvCalSendHome.asp?po=1&jx=eap&ijSchedule=' + id
       ical_text = urllib2.urlopen(ical_url).read()
       #print "ical_text: " + ical_text
       pat = re.compile('BEGIN:VALARM[\n\s\S]+END:VALARM\s')
       ical_text = re.sub(pat,'',ical_text)

       cal = icalendar.Calendar.from_string(ical_text)

       ical_event = cal.walk('vevent')[0]
      
       evt = ElmcityEventParser.Event()

       evt.title = ical_event['summary']

       evt.start_is_utc = True

       try:
         evt.location = ical_event['location']
         evt.title += ', ' + evt.location
       except:
         pass

       try:
         evt.start = ical_event['dtstart'].dt
       except:
         print traceback.print_exc()
           
       evt.url = ical_url

       uniques[evt.title + ', ' + evt.start.isoformat()] = evt

     for key in uniques.keys():

       evt = uniques[key]

       print evt

       self.events.append(evt)

def test():
  parser = LibraryInsightParser(url='http://www.libraryinsight.com/calendar.asp?jx=ea')
  parser.Parse()
  print parser.ics
  return parser.ics



