import datetime, traceback
import ElmcityEventParser

class HelloWorldParser(ElmcityEventParser.EventParser):

  def Parse(self):
    try:
      self.ParsePage()
    except: 
      self.LogMsg('exception', 'HelloWorldParser.Parse', traceback.format_exc())
    self.BuildICS()

  def ParsePage(self):
    evt = ElmcityEventParser.Event()
    evt.title = "Hello World"
    evt.start = datetime.datetime.now()
    self.events.append(evt)

def test():
  parser = HelloWorldParser(url=None)
  parser.Parse()
