clr.AddReference('System.Management')
import System.Management 

def get_approot():
  try:
    x = open('e:\\approot\\bin\\Scripts\\charts.py','r')
    bindrive = 'e:'
  except:
    bindrive = 'f:'
  return bindrive + '\\approot\\bin'

def get_resource_dirs():
  return System.IO.Directory.GetDirectories('c:\Resources\Directory')

def get_local_storage():
  return [dir for dir in get_resource_dirs() if dir.endswith('LocalStorage1')][0]
 
def get_diagnostic_store():
  return [dir for dir in get_resource_dirs() if dir.endswith('DiagnosticStore')][0] 

def get_log_storage():
  return [d for d in System.IO.Directory.GetDirectories("%s/LogFiles/Web" % get_diagnostic_store())][0]  
  
def get_failed_request_log_storage():
  return [d for d in System.IO.Directory.GetDirectories("%s/FailedReqLogFiles/Web" % get_diagnostic_store())][0]    

def show_rules(ds,log):
  rules = ds.GetAccessRules(True, True, System.Type.GetType('System.Security.Principal.NTAccount'))
  log.write('%s rules\n' % rules.Count)
  for rule in rules:
    log.write('%s %s %s\n' % (rule.IdentityReference.Value, rule.AccessControlType, rule.FileSystemRights))  

def set_permissions(directory, who, rights, inheritance, propagation, access_type):
  try:
    di = System.IO.DirectoryInfo(directory)
    ds = di.GetAccessControl()
    show_rules(ds, log)
    rule = FileSystemAccessRule(who, rights, inheritance, propagation, access_type)
    ds.AddAccessRule(rule)
    di.SetAccessControl(ds)
    show_rules(ds,log)
  except:
    log.write('cannot set permissions on %s' % directory )     

def get_process_owner():
  mos = System.Management.ManagementObjectSearcher("SELECT * from Win32_Process")
  for mo in mos.Get():
    oi = System.Array.CreateInstance(str,2)
    mo.InvokeMethod('GetOwner', oi)
    name = mo.GetPropertyValue('Name')
    if ( name == System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName ):
      return [ oi[0], oi[1] ]

def format_traceback():    
  exc_type, exc_value, exc_traceback = sys.exc_info()
  tb = traceback.format_exception(exc_type, exc_value, exc_traceback)  
  return repr(tb) 

def make_chart(local_storage, bin, source_type, chart_type, in_spec, title, query):
  try:
    GenUtils.LogMsg("info", "query: " + query, None)    
    fname = make_fname ( title, 'gif' )
    out_spec = make_out_spec( local_storage, fname )
    expanded_title = expand_title ( title )
    query = query.replace('__IN__', in_spec)
    query = query.replace('__OUT__', out_spec)
    cmd = '%s\\LogParser -q -e:1 -i:%s -o:CHART -categories:ON -groupSize:1500x800 -legend:ON -ChartTitle:"%s" -chartType:"%s" "%s"' % ( bin, source_type, expanded_title, chart_type, query )
    GenUtils.LogMsg("info", "make_chart: " + cmd, None)
    os.system(cmd)
    bs = BlobStorage.MakeDefaultBlobStorage()
    data = System.IO.File.ReadAllBytes(out_spec)
    bs.PutBlob('charts', fname, System.Collections.Hashtable(), data, "image/gif" )
  except:
    GenUtils.PriorityLogMsg('exception', 'MakeChart: ' + title, format_traceback() )

def make_out_spec(local_storage, fname):
  return '%s/%s' % ( local_storage, fname )
  
def expand_title( title ):
  return '%s - %s - %s' % ( title, System.DateTime.UtcNow.ToString(), System.Net.Dns.GetHostName() )  