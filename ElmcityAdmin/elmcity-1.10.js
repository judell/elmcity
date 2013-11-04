var host = 'http://elmcity.cloudapp.net/';
var anchor_names = [];
var today = new Date();
var last_day;
var datepicker = false;
var is_mobile = false;
var is_mobile_declared = false;
var is_mobile_detected = false;
var is_eventsonly = false;
var is_theme = false;
var is_view = false;
var is_sidebar = true;
var top_method = 0; // for use in position_sidebar


var $j = jQuery.noConflict();

var redirected_hubs = [ 'AnnArborChronicle'];

var category_images = {};
var source_images = {};

function position_sidebar(top_element)
  {
  try
    {
    var top_elt_bottom = $j('#' + top_element)[0].getClientRects()[0].bottom;
    }
  catch (e)
    {
    console.log(e.message);
    top_elt_bottom = 0;
    }

  if ( top_elt_bottom <= 0  )
     $j('#sidebar').css('top', $j(window).scrollTop() - top_offset + 'px');
  else
     $j('#sidebar').css('top', top_method);
  }


function add_mobile_switcher()
  {
  try
    {
     var switcher = '<p style="text-align:center"><a title="switch from full view to mobile" href="__HREF__">__OTHER__</a></p>';
     var href = location.href;
     remove_href_arg(href, 'mobile');
     href = add_href_arg(href, 'mobile', 'yes');
     switcher = switcher.replace("__HREF__", href);
     switcher = switcher.replace("__OTHER__", "mobile site");
     $j('#switcher').html(switcher);
     }
  catch (e)
    {
    console.log(e.message);
    }
  }

function on_load()
  {
  }


function get_elmcity_id()
  {
  return $j('#elmcity_id').text().trim();
  }

function get_view()
  {
  return $j('#view').text().trim();
  }

function get_selected_hub()
  {
  return  $j('#hub_select option:selected').val();
  }

function get_generated_hub()
  {
  return $j('#hub').text().trim();
  }


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

function parse_yyyy_mm_dd(date_str)
  {
    var match = /(\d{4,4})(\d{2,2})(\d{2,2})/.exec(date_str);
    return { year: match[1], month: match[2], day: match[3] }
  }

function parse_mm_dd_yyyy(date_str)
  {
  var match = /(\d{2,2})\/(\d{2,2})\/(\d{4,4})/.exec(date_str);
  return { month: match[1], day: match[2], year: match[3] }
  }

function parse_yyyy_mm_dd_T_hh_mm(date_str)
  {
  var match = /(\d{4,4})-(\d+)-(\d+)T(\d+):(\d+)/.exec(date_str);
  return { year: match[1], month: match[2], day: match[3], hour: match[4], minute: match[5] }
  }


function scroll(event)
  {
  if ( is_mobile || is_eventsonly )
    return;

  if ( $j('#sidebar').css('position') != 'fixed' ) // unframed, no fixed elements
    position_sidebar(top_element);

  var date_str = find_current_name().replace('d','');
  var parsed = parse_yyyy_mm_dd(date_str)
  setDate(parsed['year'], parsed['month'], parsed['day']);
  }

function find_last_day()
  {
  try
    {
    var last_anchor = anchor_names[anchor_names.length - 1];
    var parsed = parse_yyyy_mm_dd(last_anchor.replace('d',''));
    return new Date(parsed['year'], parsed['month'] - 1, parsed['day']);
    }
  catch (e)
    {
    return new Date();
    }
  }

function get_anchor_names(anchors)
  {
  var anchor_names = [];
  for (var i = 0; i < anchors.length; i++)
    {
    anchor_names.push(anchors[i].name);
    }
  return anchor_names;
  }

function day_anchors()
  {
  return $j('a[name^="d"]');
  }


function find_current_name()
  {
  if ( is_eventsonly || is_mobile ) 
    return;

//  console.log("find_current_name");

  try
    {
    var before = [];
    var datepicker_top = $j("#datepicker")[0].getClientRects()[0].top;
    var datepicker_bottom = $j("#datepicker")[0].getClientRects()[0].bottom;
    var datepicker_height = datepicker_bottom - datepicker_top;
    var datepicker_center = datepicker_top + ( datepicker_height / 2 );
    var anchors = day_anchors();
    for (var i = 0; i < anchors.length; i++)
      {
      var anchor = anchors[i];
      var anchor_top = anchor.getClientRects()[0].top;
      if ( anchor_top < datepicker_center )
        before.push(anchor.name);
      else
        break;
      }
    var ret = before[before.length-1];
    if ( typeof ret == 'undefined' )
      ret = anchors[0].name;
    }
  catch (e)
    {
     console.log("find_current_name: " + e.message);
    }
  return ret;
  }


$j(window).scroll(function(event) {
  scroll(event);
});


//$j(window).load(function () {
//  window.scrollTo(0,0);
//});

function prep_day_anchors_and_last_day()
  {
  var anchors = day_anchors();
  anchor_names = get_anchor_names(anchors);
  last_day = find_last_day();
  }


function setup_datepicker()
  {
  if ( is_eventsonly || is_mobile || datepicker ) 
     return;
//  console.log("setup_datepicker");

  prep_day_anchors_and_last_day();
  
  $j('#datepicker').datepicker({  
            onSelect: function(dateText, inst) { goDay(dateText); },
            onChangeMonthYear: function(year, month, inst) { goMonth(year, month); },
            minDate: today,
            maxDate: last_day,
            hideIfNoPrevNext: true,
            beforeShowDay: maybeShowDay
        });

  setDate(today.getFullYear(), today.getMonth() + 1, today.getDate());

  if ( $j('#sidebar').css('position') != 'fixed' ) // unframed, no fixed elements
    {
    position_sidebar(top_element)
    $j('#sidebar').css('visibility','visible');
    $j('#datepicker').css('visibility','visible');
    $j('#tags').css('visibility','visible');
    }

  datepicker = true;
  }


$j(document).ready(function(){

  var url_specified_hub = gup('hub');
  var generated_hub = get_generated_hub();

  if ( url_specified_hub != generated_hub ) // server changed it to avoid emptiness
    {
    var view = gup('view');
    //var view = '';
    var path = make_path( view );
    path = add_href_arg(path, 'hub', generated_hub );
    // $j('#picker_message').html('<span style="font-style:italic;font-size:larger">no events for category ' + view + ' in ' + url_specified_hub + ', scope expanded to all hubs</span>');
    setTimeout(function () {
       location.href = path;
       }, 3500);
    return;
    }

  var id = get_elmcity_id();

  load_category_images(id);

  load_source_images(id);

  $j('.sd').css('font-size','x-large');

  $j('.atc').css('font-size','x-large');

  var elmcity_id = get_elmcity_id();

  is_theme = gup('theme') != '';

  var view = gup('view');

  is_view = view != '';
    
  is_eventsonly = gup('eventsonly').startsWith('y');

  is_mobile_declared = gup('mobile').startsWith('y');

  is_mobile_detected = $j('#mobile_detected').text().trim() == "__MOBILE_DETECTED__";

  is_mobile = is_mobile_declared || is_mobile_detected;

  if ( is_eventsonly || is_mobile )                     
    $j('.bl').css('margin-right','3%');       // could overwrite theme-defined?

  var max_height = Math.max(screen.height,screen.width);

  if ( ! is_mobile_detected && max_height < 1000 )    
    {
    is_mobile = true;
    is_eventsonly = true;
    adjust_for_small_screen(max_height);
    }

  is_sidebar = ( ! is_mobile ) && ( ! is_eventsonly );

  if ( gup('hubtitle').startsWith('n') )
      $j('.hubtitle').remove();

  if ( gup('tags').startsWith('n') )
    $j('.cat').remove();

//  if ( gup('taglist').startsWith('n') )
//      $j('#tag_select').remove();

  if ( is_view && is_sidebar )
    try
      {
      var href = $j('#subscribe').attr('href');
      href = href.replace('__VIEW__', gup('view'));
      $j('#subscribe').attr('href',href);
      $j('#subscribe').text('subscribe');
      }
   catch (e)
      {
      }

  if ( gup('timeofday') == 'no' )
    $j('.timeofday').remove();

/*
  if ( gup('width') != '' )
    {
    $j('#body').css('width', gup('width') + 'px');
    $j('div.bl').css('margin','1em 0 0 1em');
    }
*/

//  if ( is_theme )  // invoke it
//    invoke_theme( gup('theme') );

  if ( gup('datestyle') != '' )
    apply_json_css('.ed', 'datestyle');

  if ( gup('itemstyle') != '' )
    apply_json_css('.bl', 'itemstyle');

  if ( gup('titlestyle') != '' )
    apply_json_css('.ttl', 'titlestyle');

  if ( gup('linkstyle') != '' )
    apply_json_css('.ttl a', 'linkstyle');

  if ( gup('dtstartstyle') != '' )
    apply_json_css('.st', 'dtstartstyle');

  if ( gup('sd') != '' )
    apply_json_css('.sd', 'sd');

  if ( gup('atc') != '' )
    apply_json_css('.atc', 'atc');

  if ( gup('cat') != '' )
    apply_json_css('.cat', 'cat');

  if ( gup('sourcestyle') != '' )
    apply_json_css('.src', 'sourcestyle');

  remember_or_forget_days();

//  if ( is_mobile )
//    add_fullsite_switcher();
//  else
//    add_mobile_switcher();


  if ( ! is_sidebar )
    return;

  if ( $j('#sidebar').css('position') != 'fixed' ) // unframed, no fixed elements
    setTimeout('setup_datepicker()', 200);
  else
    setup_datepicker(); 

/*
  if ( ! is_eventsonly ) 
    for ( i = 0; i < 7; i++ )  
        show_desc('e'+ i);
*/

  });


function apply_json_css(element,style)
  {
  try 
    {
    var style = decodeURIComponent(gup(style));
    style = style.replace(/'/g,'"');
    $j(element).css(JSON.parse(style));
    }
  catch (e)
    {
    console.log(e.message);
    }
  }

function scrollToElement(id) 
  {
  window.scrollTo(0, $j('#' + id).offset().top);
  }


function setDate(year,month,day)
  {
//  console.log("set_date");
  var date =  $j('#datepicker').datepicker('getDate');
  var current_date = $j('td > a[class~=ui-state-active]');
  current_date.css('font-weight', 'normal');
  $j('#datepicker').datepicker('setDate', new Date(year, month-1, day));
  var td = $j('td[class=ui-datepicker-current-day] > a[class~=ui-state-active]');
  var td = $j('td > a[class~=ui-state-active]');
  current_date = $j('td > a[class~=ui-state-active]');
  current_date.css('font-weight', 'bold');
  }


function maybeShowDay(date)
  {
  var year = date.getFullYear();
  var month = date.getMonth() + 1;
  var day = date.getDate();
  month = maybeZeroPad(month.toString());
  day = maybeZeroPad(day.toString());
  var date_str = "d" + year + month + day;
  show = $j.inArray( date_str, anchor_names ) == -1 ? false : true;
  var style = ( show == false ) ? "ui-datepicker-unselectable ui-state-disabled" : "";
  return [show, style]
  }

function goDay(date_str)
  {
  var parsed = parse_mm_dd_yyyy(date_str)
  var year = parsed['year'];
  var month = parsed['month'];
  var day = parsed['day'];
  var id = 'd' + year + month + day;
  scrollToElement(id);
//  location.href = '#d' + year + month + day;
//  setDate(year, month, day);
  }

function goMonth(year, month)
  {
  month = maybeZeroPad(month.toString());
  var id = $j('h1[id^="d' + year + month + '"]').attr('id')
  scrollToElement(id);
//  location.href = '#ym' + year + month;
//  setDate(year, parseInt(month), 1);
  }

function maybeZeroPad(str)
  {
  if ( str.length == 1 ) str = '0' + str;
  return str;
  }

function remove(array, str)
   {
    for(var i=0; i<array.length; i++) 
      {
      if ( array[i] == str || array[i].startsWith(str) ) 
        {
        array.splice(i, 1);
        break;
        }
      }
   }

Date.prototype.addDays = function(days) {
    this.setDate(this.getDate()+days);
} 

String.prototype.replaceAt=function(index, char) {
    return this.substr(0, index) + char + this.substr(index+char.length);
}

String.prototype.startsWith = function (str){
    return this.indexOf(str) == 0;
};

String.prototype.contains = function (str){
    return this.indexOf(str) != -1;
};

String.prototype.endsWith = function (str){
    return this.indexOf(str) == this.length - str.length - 1;
};

if(!String.prototype.trim) {
  String.prototype.trim = function () {
    return this.replace(/^\s+|\s+$/g,'');
  };
}


function case_insensitive_sort(a, b) 
  {
  var x = a.toLowerCase();
  var y = b.toLowerCase();
  return ((x < y) ? -1 : ((x > y) ? 1 : 0));
  }

function make_path(view)
  {
  var path;
  var elmcity_id = get_elmcity_id();
  if ( view == undefined )
    {
    var selected = $j('#tag_select option:selected').val();
    view = selected.replace(/\s*\((\d+)\)/,'');
    path = make_view_path_from_picklist(view, elmcity_id);
    }
  else
    {
    path = make_view_path_from_view(view, elmcity_id);
    }

  if ( gup('test') != '')
    path = add_href_arg(path,'test',gup('test') );

  if ( gup('theme') != '')
    path = add_href_arg(path,'theme',gup('theme') );

  if ( gup('count') != '')
    path = add_href_arg(path,'count',gup('count') );

  if ( gup('mobile') != '')
    path = add_href_arg(path,'mobile',gup('mobile') );

  if ( gup('eventsonly') != '')
    path = add_href_arg(path,'eventsonly',gup('eventsonly') );

  if ( gup('template') != '')
    path = add_href_arg(path,'template',gup('template') );

  if ( gup('jsurl') != '')
    path = add_href_arg(path,'jsurl',gup('jsurl') );

  if ( gup('hub') != '') 
    path = add_href_arg(path,'hub', get_selected_hub() );

   try
     {
     var days_cookie_name = make_cookie_name_from_view(view);
     var days_cookie_value = $j.cookie(days_cookie_name);
     if ( typeof(days_cookie_value)!='undefined'  )
       {
       var days = days_cookie_value;
       path = add_href_arg( path, 'days', days );
       }
     }
   catch (e)
     {
     console.log(e.message);
     }   

  return path;
  }

function show_view(view)
  {
  var elmcity_id = get_elmcity_id();

  var path = make_path(view);

  location.href = path;
  }

function make_view_path_from_view(view, elmcity_id)
  {
  var path;
  if ( redirected_hubs.indexOf(elmcity_id) == -1 )
    path = '/' + elmcity_id + '/?view=' + encodeURIComponent(view);
  else
    path = '/html?view=' + encodeURIComponent(view);
  return path;
  }

function make_view_path_from_picklist(view, elmcity_id)
  {
  var path;
  if ( redirected_hubs.indexOf(elmcity_id) == -1 )
    {
    if ( view == 'all' )
      path = '/' + elmcity_id + '/';
    else
      path = '/' + elmcity_id + '/?view=' + encodeURIComponent(view);
    }
  else
    {
    if ( view == 'all' )
      path = '/html' + '/';
    else
      path = '/html?view=' + encodeURIComponent(view);
    }
  return path;
  }


function remove_href_arg(href, name)
  {
  var pat = eval('/[\?&]*' + name + '=[^&]*/'); 
  href = href.replace(pat,'');
  if ( (! href.contains('?')) && href.contains('&') )
    href = href.replaceAt(href.indexOf('&'),'?');
  return href;
  }

function add_href_arg(href, name, value)
  {
  href = remove_href_arg(href,name);
  if ( href.contains('?') )
    href = href + '&' + name + '=' + value;
  else
    {
    href = href + '?' + name + '=' + value;
    }
  return href;
  }


function dismiss_menu(id)
{
var elt = $j('#' + id);
elt.find('.menu').remove();
}

function get_add_to_cal_url(id,flavor)
{
var elt = $j('#' + id);
var start = elt.find('.st').attr('content');
var end = ''; // for now
var url = elt.find('.ttl').find('a').attr('href');
var summary = get_summary(id);
var description = elt.find('.src').text();
var location = ''; // for now

var elmcity_id = get_elmcity_id();

var service_url = host + 'add_to_cal?elmcity_id=' + elmcity_id + 
                            '&flavor=' + flavor + 
                            '&start=' + encodeURIComponent(start) + 
                            '&end=' + end +
                            '&summary=' + encodeURIComponent(summary) +
                            '&url=' + encodeURIComponent(url) +
                            '&description=' + encodeURIComponent(description) +
                            '&location=' + location;
return service_url;
}


function add_to_google(id)
{
try
  {
  var service_url = get_add_to_cal_url(id, 'google');
  $j('.menu').remove();
//  console.log('redirecting to ' + service_url);
//  location.href = service_url;
  window.open(service_url, "add to google");
  }
catch (e)
  {
  console.log(e.message);
  }
}

function add_to_hotmail(id)
{
var service_url = get_add_to_cal_url(id, 'hotmail');
$j('.menu').remove();
location.href = service_url;
}

function add_to_ical(id)
{
var service_url = get_add_to_cal_url(id, 'ical');
$j('.menu').remove();
location.href = service_url;
}

function add_to_facebook(id)
{
var service_url = get_add_to_cal_url(id, 'facebook');
$j('.menu').remove();
location.href = service_url;
}


function add_to_cal(id)
{
elt = $j('#' + id);
quoted_id = '\'' + id + '\'';
elt.find('.menu').remove();
elt.append(
  '<ul class="menu">' + 
  '<li><a title="add this event to your Google calendar" href="javascript:add_to_google(' + quoted_id + ')">add to Google Calendar</a></li>' +
  '<li><a title="add this event to your Hotmail calendar" href="javascript:add_to_hotmail(' + quoted_id + ')">add to Hotmail Calendar</a></li>' +
  '<li><a title="add to your Outlook, Apple iCal, or other iCalendar-aware desktop calendar" href="javascript:add_to_ical(' + quoted_id + ')">add to iCal</a></li>' +
  '<li><a title="add to Facebook (remind yourself and invite friends with 1 click!)" href="javascript:add_to_facebook(' + quoted_id + ')">add to Facebook</a></li>' +
  '<li><a title="dismiss this menu" href="javascript:dismiss_menu(' + quoted_id + ')">cancel</a></li>' + 
  '</ul>'
  );
}

var current_id;

function active_description(description)
{

description = description.replace(/<br>\s+/g, '<br>')
description = description.replace(/(<br>)\1+/g, '<br><br>')


quoted_id = '\'' + current_id + '\'';

var cat_images = "";

var source_image = "";

var all_images = new Array();

try {
  var source = $j('#' + current_id + ' .src').text();

//  all_images.push(source_images[source]);

  var img_url = source_images[source];
    if ( typeof (img_url) != 'undefined' && img_url.contains('NoCurrentImage') == false )
      source_image += '<a title="' + source + '"><img alt="' + source + '" style="float:left;margin:8px;width:100px" src="' + img_url + '"></a>'
  }
catch (e) {
  console.log(e.message);
  }



try {
//  var first_cat = $j('#' + current_id + ' .cat a')[0].firstChild.textContent;
  var cats = $j('#' + current_id + ' .cat a');
  for ( i = 0; i < cats.length; i++ )
    {
    var cat = cats[i].firstChild.textContent;

/*    
    if ( all_images.indexOf(category_images[cat]) != -1 )
      continue;
    else
      all_images.push(category_images[cat]);
*/

    if ( cat == 'facebook' )
      continue;
   
    var img_url = category_images[cat];
    if ( typeof (img_url) != 'undefined' && img_url.contains('NoCurrentImage') == false )
      {
      var href = location.href;
      href = add_href_arg(href, 'view', cat);
      var message;
      var href_html;
      if ( gup('view') != cat )
         {
         message = 'switch to the ' + cat + ' view';
         href_html = 'href="' + href + '"';
         }
      else
         {
         message = cat;
         href_html = ' ';
         }
      cat_images += '<a title="' + message + '"' + href_html + '><img alt="' + cat + '" style="float:left;margin:8px;width:100px" src="' + img_url + '"></a>';
      }
    if ( i > 2 ) 
       break;
    }
  }
catch (e) {
  console.log(e.message);
  }


description = source_image + cat_images + description;
 

//s.match( /(\d+-\d+-)(\d+)(T\d+:\d+)/ )
//["2013-10-07T19:00", "2013-10-", "07", "T19:00"]

/*
try {
  var dstr = get_dtstart(current_id);
  var m = dstr.match( /(\d+-\d+-)(\d+)(T\d+:\d+)/ );
  var date_key = m[1] + '**' + m[3];
  var location_key = get_summary(current_id) + date_key;
  var latlon = meetup_locations[location_key];
  if ( typeof (latlon ) != 'undefined' ) {
    var date = get_md(current_id) + ' ' + get_st2(current_id);
    var title = get_summary(current_id);
    var map_url = 'http://elmcity.cloudapp.net/get_blob?id=admin&path=map_detail.html?lat=' + latlon[0] + '&lon=' + latlon[1] + '&title=' + encodeURIComponent(title) + '&date=' + encodeURIComponent(date);
    var iframe = '<iframe src="' + map_url + '" width="100%" height="400" style="margin-top:20%;border-width:thin;border-style:solid;border-color:slategray" scrolling="no" seamless="seamless"></iframe>';
    description = description + iframe;
    }
  }
catch (e) {
  console.log(e.message);
  }
*/

try {
  var lat = $j('#' + current_id + ' span[property="v:latitude"]').attr('content');
  var lon = $j('#' + current_id + ' span[property="v:longitude"]').attr('content');
  if ( typeof (lat) != 'undefined' && typeof(lon) != 'undefined' ) {
    var date = get_md(current_id) + ' ' + get_st2(current_id);
    var title = get_summary(current_id);
    var map_url = 'http://elmcity.cloudapp.net/get_blob?id=admin&path=map_detail.html?lat=' + lat + '&lon=' + lon + '&title=' + encodeURIComponent(title) + '&date=' + encodeURIComponent(date);
    var iframe = '<iframe style="margin-top:20px" src="' + map_url + '" width="100%" height="400" scrolling="no" seamless="seamless"></iframe>';
    description = description + iframe;
    }
  }
catch (e) {
  console.log(e.message);
  }

var x = '<span style="font-size:larger;float:right;"><a title="hide description" href="javascript:hide_desc(' + quoted_id + ')"> [X] </a> </span>';


var s = '<div style="overflow:hidden;text-indent:0;border-style:solid;border-width:thin;padding:8px;margin:8px" id="' + current_id + '_desc' + '">' + x + '<div>' + description + '</div></div>';

elt = $j('#' + current_id);

s = s.replace('<br><br>','<br>');


elt.append(s);
}



function show_more(id)
  {
  $j('div.' + id).show();
  $j('span.' + id).remove();
  }

function hide_desc(id)
{
quoted_id = '\'' + id + '\'';

$j('#' + id + '_desc').remove();
$j('#' + id + ' .sd').css('display','inline');
$j('#' + id + ' .atc').css('display','inline');
}


function show_desc(id)
{
quoted_id = '\'' + id + '\'';

$j('#' + id + ' .sd').css('display','none');
$j('#' + id + ' .atc').css('display','none');


var _dtstart = get_dtstart(id);
var _title = get_summary(id);
var elmcity_id = get_elmcity_id();
_active_description = "";
var url = host + elmcity_id + '/description_from_title_and_dtstart?title=' + encodeURIComponent(_title) + '&dtstart=' + _dtstart + '&jsonp=active_description';

current_id = id;

$j.getScript(url);
}

function on_load()
  {
  }

function find_id_of_last_event()
  {
  var events = $j('.bl');
  var last = events[events.length-1];
  return last.attributes['id'].value;
  }

function get_summary(id)
  {
  var elt = $j('#' + id);
  var summary = $j('#' + id + ' .ttl span').text();
  if ( summary == '')
    summary = $j('#' + id + ' .ttl a').text();
  return summary;
  }

function get_dtstart(id)
  {
  return $j('#' + id + ' .st').attr('content');
  }

function get_md(id)
  {
  return $j('#' + id + ' span[class="md"').text()
  }

function get_st(id)
  {
  return $j('#' + id + ' .st').attr('content');
  }

function get_st2(id)
  {
  return $j('#' + id + ' .st').text();
  }

$j.extend({
    keys:  function(obj){
        var a = [];
        $j.each(obj, function(k){ a.push(k) });
        return a;
      }
   });

function remember_or_forget_days()
  {
  var view = gup('view');
  var days = gup('days');

  if ( days != '' )
    remember_days(view, days);
  else
    forget_days(view);
  }

function remember_days(view, days)
  {
  try
    {
    var cookie_name = make_cookie_name_from_view(view);
    $j.cookie(cookie_name, days);
    }
  catch (e)
    {
    console.log(e.message);
    }
  }

function forget_days(view)
  {
  try
    {
    var cookie_name = make_cookie_name_from_view(view);
    $j.removeCookie(cookie_name);
    }
  catch (e)
    {
    console.log(e.message);
    }
  }

function make_cookie_name_from_view(view)
  {
  if ( view == 'all' )
     view = '';
  view = view.replace(',' , '_');       
  view = view.replace('-' , '_minus_'); 
  var cookie_name = 'elmcity_' + view + '_days';
  return cookie_name; 
  }

function load_category_images(id)
{
  $j.ajax({
       // http://elmcity.cloudapp.net/get_blob?id=ChesapeakeVA&path=category_images.json
       url: 'http://elmcity.cloudapp.net/get_blob?id=' + id + '&path=category_images.json',
       cache: false,

       complete: function(xhr, status) { 
           try {
            category_images = JSON.parse(xhr.responseText);
            var view = gup('view');
            if ( typeof(category_images[view]) != 'undefined' && category_images[view] != 'http://elmcity.blob.core.windows.net/admin/NoCurrentImage.jpg' ) 
               {
               var href = location.href;
               $j('#category_image')[0].innerHTML = '<img style="width:140px;border-width:thin;border-style:solid" src="' + category_images[view] + '">'; 
               }                
            else
               $j('#category_image')[0].innerHTML = '';
             }
           catch (e) {
              console.log('no category images' + e.message);
             }
          }
       });
  }

function load_source_images(id)
{
  $j.ajax({
       // http://elmcity.cloudapp.net/get_blob?id=ChesapeakeVA&path=meetup_locations.json
       url: 'http://elmcity.cloudapp.net/get_blob?id=' + id + '&path=source_images.json',
       cache: false,

       complete: function(xhr, status) { 
           try {
             source_images = JSON.parse(xhr.responseText);
             }
           catch (e) {
              console.log('no source images' + e.message);
             }
          }
       });

  }



