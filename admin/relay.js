Here's a first cut at a deep linking solution:

1. put this into a file called relay.js at the bottom of the page

function gup( name )
  {  
  name = name.replace(/[\[]/,"\\\[").replace(/[\]]/,"\\\]");  
  var regexS = "[\\?&]"+name+"=([^&#]*)";  
  var regex = new RegExp( regexS );  
  var results = regex.exec( window.location.href );   
  if( results == null )    
    return "";  
  else    
    return results[1].replace(/%20/,' ');
  }


var src = $('iframe').attr('src');

if ( gup('view') )
  {
  src = src + '?view=' + gup('view');
  $('iframe').attr('src',src)
  }
  

2. add this to the bottom of the page

<script src="relay.js"></script>

You can see it in use here:

http://jonudell.net/elmcity/a2chron_orig.html?view=knitting

If this works OK for you, there some enhancements I'd do. 

- Add a timer-based function to watch for changes to the embedded iframe's URL and reflect them to the containing URL. I.e. now, if you go to http://annarborchronicle.com/events-listing/?view=music and then switch inside the frame to the knitting category, the containing URL won't change. The embedded frame doesn't have the power to reach outside itself and change the containing URL. But an enhancement to relay.js could watch for changes and reflect them.

- Handle all the other parameters that could be sent to the inner frame: count, from, to, days, hours, etc (see http://elmcity.cloudapp.net/AnnArborChronicle/about -> Guide to URLs for examples)

OTOH if you do away with the embedded frame and map events.annarborchronicle.com directly to the service, it's all moot.

Cheers,

Jon


--

PS: While poking around in that clone of your page I noticed a few minor anomalies that might want to get looked at:

1. A pair of scripts, /js/plugins.js and /js/script.js, are referenced (from every a2chron page I think) but never found.

2. The containing page sources 3 (!) copies of jQuery -- and that's not including the one used in the embedded frame on the events page. The ones in the container are:

- base.js, an old (1.4.2) jQuery
- jquery.js, which is jQuery 1.7.2 (minified, though it doesn't say so)
- jquery.min.js, which is jQuery.1.7.2 again

