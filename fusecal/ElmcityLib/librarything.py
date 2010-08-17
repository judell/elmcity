import ElmcityEventParser 

from BeautifulSoup import BeautifulSoup
import sys, datetime, re, urllib2, xml.dom.minidom, traceback

class LibraryThingParser(ElmcityEventParser.EventParser):

  def Parse(self):

    try:
      self.LogMsg("info", "LibraryThingParser.Parse", None)
      self.ParsePage() 
    except:
      self.LogMsg('exception', 'LibraryThingParser.Parse', traceback.format_exc())

    self.BuildICS()    

  def ParsePage(self):

    rss = urllib2.urlopen(self.url).read()
    xmldoc = xml.dom.minidom.parseString(rss)

    if not xmldoc:
      return

    for node in xmldoc.getElementsByTagName('item'):

        evt = ElmcityEventParser.Event()

        #add event title and summary, handles more than 1 : in the title tag
        #(ex. "Seminary Co-op Bookstore: Mark Weiss discusses and signs The Whole Island: Six Decades of Cuban Poetry")

        title = node.getElementsByTagName('title')[0].firstChild.nodeValue.split(": ")
        locName = title[0].strip()
        eventTitle = ": ".join(title[1:]).strip()

        locationLink = node.getElementsByTagName('link')[0].firstChild.nodeValue

        description = node.getElementsByTagName('description')[0].firstChild.nodeValue

        evt.location = locName +  " " + self.ExtractEventLocation(locationLink)

        evt.title = eventTitle + ', ' + evt.location

        evt.start = self.ExtractEventDateTime(description)

        print evt

        self.events.append(evt)


  def ExtractEventDateTime(self,eventStr):

    #returns a string like September 26 at 3:45pm
    dateTime_reg = re.compile("<b>(.*)</b>")
    dateTime = re.search(dateTime_reg, eventStr).groups()[0]
   
    #parses dateTime to get month, day, hour, and minute
    date, time = dateTime.split(" (");
    time = time[:-1]

    month, day = date.split(", ")
    month, day = day.split(" ")

    hour,minute = time.split(":")
    amPm = minute[3:]
    minute = minute[:2]


    if amPm.upper() == 'PM' and int(hour) < 12:
        hour = int(hour) + 12

    elif amPm.upper() == 'AM' and int(hour) >= 12:
        hour = int(hour) - 12

    currDateTime = datetime.datetime.now()

    year = currDateTime.year
    if currDateTime.month > self.month_dict[month]:
        year += 1

    dateTime = datetime.datetime(year,self.month_dict[month],int(day),int(hour),int(minute))

    return dateTime


  def ExtractEventLocation(self,locationUrl):

    location_page = urllib2.urlopen(locationUrl)
    soup = BeautifulSoup(location_page)
    element_soup = soup.find(attrs={'class' : re.compile("^venueAddress$")})

    return self.ParseLocationString(element_soup)
   
  def ParseLocationString(self,element_soup):

    try:
        street_reg = re.compile("<div class=\"venueAddress\">(.*)<a")
        street = re.search(street_reg, element_soup).groups()[0]
    except:
        street = ""
   
    try:
        city_reg = re.compile('<a href=".*"><br />(.*)</a>')
        city = re.search(city_reg, element_soup).groups()[0]
    except:
        city = ""
   
    return street+" "+city

def test():
  parser = LibraryThingParser(url='http://www.librarything.com/rss/events/location/toronto',tz_source='eastern')
  parser.Parse()
