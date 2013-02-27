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

function getScript(url,success)
  {
//  console.log('getScript');
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
    'http://ajax.aspnetcdn.com/ajax/jQuery/jquery-1.7.2.min.js',
    function() 
      {
      if (typeof jQuery=='undefined') 
        {
        msg='Sorry, but jQuery wasn\'t able to load';
        } 
      else 
        {
        msg='This page is now jQuerified with v' + jQuery.fn.jquery;
        };
      return showMsg();
      }
    );

function showMsg() 
  {
//  console.log('showMsg');
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
          $ = jQuery.noConflict();
          }
		}
      run();
	  },
	2500
    );    
  }


function run() 
  {
  //alert('run');
  $.ajax
      (
        {
        url: '/',
        complete: find_gcal_ical_url
        }
      );
    }

var done = false;

function find_gcal_ical_url(xhr, status)
  {
  if ( done ) return;
  var iframes = $('iframe');
  for ( i = 0; i < frames.length; i++ )
	{
	var src = iframes[i].src;
	if ( iframes[i].src.indexOf('google.com') > 0  && iframes[i].src.indexOf('calendar') > 0 )
        {
		var addrs = src.match('src=([^&]+)&','g');
        for ( j = 0; j < addrs.length; j++ )
			{
			$('body').prepend('<h1 style="font-size:13pt;font-weight:normal;color:black;background-color:white;font-family:arial">Google Calendar iCal URL: http://www.google.com/calendar/ical/' + addrs[j].replace('src=','').replace('&','') + '/public/basic.ics</h1>');
			done = true;
			}
        }
	}
  }

