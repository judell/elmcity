// begin http://www.learningjquery.com/2009/04/better-stronger-safer-jquerify-bookmarklet/

var el=document.createElement('div'), b=document.getElementsByTagName('body')[0],  otherlib=false,  msg='';
el.style.position='fixed';
el.style.height='32px';
el.style.width='220px';
el.style.marginLeft='-110px';
el.style.top='0';
el.style.left='50%';
el.style.padding='5px 10px';
el.style.zIndex = 1001;
el.style.fontSize='12px';
el.style.color='#222';
el.style.backgroundColor='#f99';
 
if(typeof jQuery!='undefined') 
  {
  msg='This page already using jQuery v'+jQuery.fn.jquery;
  showMsg();
  } 
else if (typeof $ == 'function') 
  {
  otherlib=true;
  }

var hcid;
var calendar_dict = {};
var published_cal_name;
var published_cal_url;
 
function getScript(url,success)
  {
  var script=document.createElement('script');
  script.src=url;
  var head=document.getElementsByTagName('head')[0], done=false;
    // Attach handlers for all browsers
  script.onload=script.onreadystatechange = 
    function()
      {
      if ( !done && (!this.readyState || this.readyState == 'loaded' || this.readyState == 'complete') ) 
        {
        done=true;
        success();
        script.onload = script.onreadystatechange = null;
        head.removeChild(script);
        }
      };
  head.appendChild(script);
  }

getScript
  (
    'http://ajax.microsoft.com/ajax/jQuery/jquery-1.4.2.js',
    function() 
      {
      if (typeof jQuery=='undefined') 
        {
        msg='Sorry, but jQuery wasn\'t able to load';
        } 
      else 
        {
        msg='This page is now jQuerified with v' + jQuery.fn.jquery;
        if (otherlib) 
          { 
          //msg +=' and noConflict(). Use $jq(), not $().';
          }
        };
      return showMsg();
      }
    );

function showMsg() 
  {
  el.innerHTML=msg;
  b.appendChild(el);
  window.setTimeout
    (
	function()
	  {
      if (typeof jQuery=='undefined')
        {
        b.removeChild(el);
        } 
      else 
        {
        jQuery(el).fadeOut
        (
        'slow',
        function() {jQuery(this).remove();}
        );
        if (otherlib) 
          {
          $jq = jQuery.noConflict();
          }
		}
      run();
	  },
	2500
    );    
  }


// end http://www.learningjquery.com/2009/04/better-stronger-safer-jquerify-bookmarklet/

function run() 
  {
  //alert('run');
  $jq.ajax
      (
        {
        url: '/',
        complete: gather_info
        }
      );
    }

String.prototype.startsWith =
  function(str)
    {
    return ( this.match("^" + str) == str) ; 
    };

function get_cookie_pair_val(key)
  {
  ret = '';
  pairs = document.cookie.split('; ');
  for (i=0; i<pairs.length;i++)
    {
    var pair = pairs[i]; 
    if ( pair.startsWith(key) )
      {
      var pair_val = pair.split('=');
      ret = pair_val[1];
      }
    }
//  console.log('returning pair val: ' + ret)
  return ret;
  }

function get_checked_radio()
  {
  for (i=0;i<document.calendars.radios.length;i++) 
    {
    var radio = document.calendars.radios[i];
	if (radio.checked) 
      return radio;
	}
  }	

function publish()
  {
  checked_radio = get_checked_radio();
  published_cal_name = calendar_dict[checked_radio.value];
  published_cal_url = 'http://cid-' + hcid + '.calendar.live.com/calendar/' + published_cal_name + '/calendar.ics';
  $jq.ajax
    (
      {
       beforeSend: add_mt_header,
       url: '/calendar/cal.fpp?cnmn=Microsoft.Live.Calendar.FppPresentation.ServiceProxy.SaveCalendarSharingSettings',
       type: 'POST',
       data: 'cn=Microsoft.Live.Calendar.FppPresentation.ServiceProxy&mn=SaveCalendarSharingSettings&d=%22' + checked_radio.value + '%22,true,false,true,[],[],[],20,30,0&v=1'
       }
    );
  alert('Done! "' + published_cal_name + '" is published to ' + published_cal_url);
  location.href="/";
  }

function add_mt_header(xhr, settings)
  {
  console.log('add_mt_header');
  var mt_header = get_cookie_pair_val('mt=');
  xhr.setRequestHeader('mt', mt_header);
  }


function emphasize_url(selected)
  {
  console.log('emphasize_url');
  checked_radio = get_checked_radio();
  $jq('.elmcity_row > td > span').css('font-weight','normal');
  $jq('#' + checked_radio.value).css('font-weight','bold');
  }

function cancel()
  {
  location.href="/";
  }


function gather_info(xhr, status)
  {
  var cals = $jq('li.calendarItem > a.CalendarPickerCalendarName');
  var spans = $jq('li.calendarItem > a.CalendarPickerCalendarName > span');

  for ( i = 0; i < cals.length; i++ )
    {
    var guid = cals[i].attributes['onclick'].value.match(/'calendarGuid':'(.+)'/)[1];
    var text = spans[i].innerText;
    calendar_dict[guid] = text;
    }

  var hcid_script = $jq('script:contains("hcid")');
  if ( hcid_script.length == 1 )
    {
    var hcid_text = hcid_script.text();
    hcid = hcid_text.match(/"hcid":"(.+)"/)[1];
	}
  else
    {
	var scripts = $jq('script');
	for ( i = 0; i < scripts.length; i++ )
      {
      var hcid_text = scripts[i].text;
      var match = hcid_text.match(/"hcid":"(.+)"/);
      if ( match != null )	  
        hcid = match[1];
      }
    }
  
  var s = '<div class="calendarPickerContainer" style="padding:10px;border-width:thin;border-style:solid;width:70%;margin-left:auto;margin-right:auto;"><div id="elmcity_dialog"><p>1. select a calendar, 2. copy its url, 3. click publish, 4. paste the url into an email to your elmcity curator</p><form name="calendars" onsubmit="javascript:publish()">';

  s += '<table>';
  for ( o in calendar_dict )
    {
    var key = o;
    var text = calendar_dict[o];
    var calname = calendar_dict[key];
    var ical_url = 'http://cid-' + hcid + '.calendar.live.com/calendar/' + calname + '/calendar.ics';
	ical_url = ical_url.replace(/ /g,'+');
    s += '<tr class="elmcity_row"><td><input onclick="javascript:emphasize_url()" name="radios" type="radio" value="' + key + '" />' + text + '</td>';
	s += '<td><span id="' + key + '">' + ical_url + '</span></td></tr>';
    }
  s += '</table>';
  s += '<p><input type="submit" value="publish"> <input onclick="javascript:cancel()" type="submit" value="cancel"></p>';
  s += '</form></div></div>';

  $jq('body').prepend(s);
  }

