import ElmcityEventParser

from BeautifulSoup import BeautifulSoup
import re, urllib2, datetime, traceback

class MySpaceParser(ElmcityEventParser.EventParser):

  def Parse(self):

    try:
      self.LogMsg("info", "MySpaceParser.Parse", None)
      self.ParsePage()
    except:
      self.LogMsg('exception', 'MySpaceParser.Parse', traceback.format_exc())

    self.BuildICS()

  def ParsePage(self):

    html = self.GetTourPage()

    if not html:
      return 

    soup = BeautifulSoup(html)
    items = soup.findAll("div", {'class' : 'eventitem'} )

    for item in items:
      
      title = item.findAll('div', { 'class' : 'event-titleinfo' })[0].text
      title = title.replace('&nbsp;','')

      url = item.findAll('a', { 'href' : True } )[0]['href']

      start = item.findAll('div', { 'class' : 'event-cal' })[0].text

      start = self.ExpandTodayAndTomorrow(start)   # normalize to, e.g., Thu, July 08 10:00 PM

      #tstart = time.strptime(start, '%a, %B %d @ %I:%M %p')
      dtstart = self.ParseDateTime(start, '%a, %B %d @ %I:%M %p')


      this_year = datetime.datetime.now().year
      this_month = datetime.datetime.now().month

      if ( dtstart.month < this_month ):
        year = this_year + 1
      else:
        year = this_year

      month = dtstart.month
      day   = dtstart.day
      hour  = dtstart.hour
      min   = dtstart.minute
      sec   = dtstart.second

      dtstart = datetime.datetime(year,month,day,hour,min,sec)
      
      evt = ElmcityEventParser.Event()
      evt.title = title
      evt.start = dtstart
      evt.url = url

      print evt

      self.events.append(evt)


  def ExpandTodayAndTomorrow(self,start):

    format = '%a, %B %d'

    if ( start.find('Today') == 0 ):
      dt = datetime.datetime.today()
      start = start.replace('Today', dt.strftime(format))

    if ( start.find('Tomorrow') == 0 ):
      dt = datetime.datetime.today() + datetime.timedelta(1)
      start = start.replace('Tomorrow', dt.strftime(format))

    return start    

  def GetTourPage(self):
      html = urllib2.urlopen(self.url).read()
      soup = BeautifulSoup(html)
      div = soup.find("div", {"id":"profile_bandschedule"})
      if (div):
          a = div.find("a")
          url = a.get("href")
          html = urllib2.urlopen(url).read()
          return html
      else:
          return ""

def test():
  parser = MySpaceParser(url='http://www.myspace.com/jatobamusic',filter='Vermont',tz_source='eastern')
  parser.Parse()




