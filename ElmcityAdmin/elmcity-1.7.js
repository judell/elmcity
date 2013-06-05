var host = 'http://elmcity.cloudapp.net/';
var anchor_names = [];
var today = new Date();
var last_day;
var datepicker = false;
var long;


function rescale()
  {
    long = $('#mobile_long').text().trim();
    $('.ed').css('font-size','80%');    
    $('.ttl').css('font-size','100%');    
    $('.st').css('font-size','100%');    
    $('.src').css('font-size','100%');    
    $('.bl').addClass('_bl');
    $('.bl').removeClass('bl');
    $('._bl').css('margin-bottom','3%');
    $('.ed').css('margin-top','4%');
    $('.timeofday').remove();
  }

function alt()
  {
//  long = 800;
  }

function is_theme()
  {
  return gup('theme') != '';
  }

function is_eventsonly()
  {
  return gup('eventsonly') == 'yes';
  }

function is_view()
  {
  return gup('view') != '';
  }


function add_fullsite_switcher()
  {
  try
    {
    var href = location.href;
    remove_href_arg(href, 'mobile');
    href = add_href_arg(href, 'mobile', 'no');
    var long = $('#mobile_long').text().trim();
    var switcher = '<p class="sidebar" style="text-align:center"><a title="switch from full view to mobile" href="__HREF__">__OTHER__</a></p>';
    switcher = switcher.replace("__HREF__", href);
    switcher = switcher.replace("__OTHER__", "full site");
    $('body').append(switcher);
    id_scale('switcher');
    }
  catch (e)
    {
    console.log(e.description);
    }
  }

function position_sidebar(top_element)
  {
  var top_elt_bottom = $('#' + top_element)[0].getClientRects()[0].bottom;
 
  if ( top_elt_bottom <= 0  )
     $('#sidebar').css('top', $(window).scrollTop() - top_offset + 'px');
   else
     $('#sidebar').css('top', '0px');
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
     $('#switcher').html(switcher);
     }
  catch (e)
    {
    console.log(e.description);
    }
  }

function on_load()
  {
  }


function get_elmcity_id()
  {
  return $('#elmcity_id').text().trim();
  }

function is_mobile()
  {
  var is_mobile = false;

  if ( $('#mobile').text().trim() == 'yes' && ! gup('mobile').startsWith('n') )  // detected and not refused
    is_mobile = true;

  if ( gup('mobile').startsWith('y') )                         // declared
    is_mobile = true;

  return is_mobile;
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

function scroll(event)
  {
  if ( is_mobile() || is_eventsonly() )
    return;

  if ( $('#sidebar').css('position') != 'fixed' ) // unframed, no fixed elements
    position_sidebar(top_element);

  var date_str = find_current_name().replace('d','');
  var parsed = parse_yyyy_mm_dd(date_str)
  setDate(parsed['year'], parsed['month'], parsed['day']);
  }

function find_last_day()
  {
  if ( ! is_mobile() )
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
  return $('a[name^="d"]');
  }


function find_current_name()
  {
  if ( is_eventsonly() || is_mobile() ) 
    return;

//  console.log("find_current_name");

  try
    {
    var before = [];
    var datepicker_top = $("#datepicker")[0].getClientRects()[0].top;
    var datepicker_bottom = $("#datepicker")[0].getClientRects()[0].bottom;
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
     console.log("find_current_name: " + e.description);
    }
  return ret;
  }


$(window).scroll(function(event) {
  scroll(event);
});


//$(window).load(function () {
//  window.scrollTo(0,0);
//});

function prep_day_anchors_and_last_day()
  {
  var anchors = day_anchors();
  anchor_names = get_anchor_names(anchors);
  last_day = find_last_day();
  }

function morelink()
  {
  return '<a title="' + $('a[name^="d"]').length + ' days included, click to add 2 weeks" href="javascript:more()">more</a>';
  }

function setup_datepicker()
  {
  if ( datepicker )
     return;
//  console.log("setup_datepicker");

  prep_day_anchors_and_last_day();
  
  $('#datepicker').datepicker({  
            onSelect: function(dateText, inst) { goDay(dateText); },
            onChangeMonthYear: function(year, month, inst) { goMonth(year, month); },
            minDate: today,
            maxDate: last_day,
            hideIfNoPrevNext: true,
            beforeShowDay: maybeShowDay
        });

  setDate(today.getFullYear(), today.getMonth() + 1, today.getDate());

  if ( $('#sidebar').css('position') != 'fixed' ) // unframed, no fixed elements
    {
    position_sidebar(top_element)
    $('#sidebar').css('visibility','visible');
    $('#datepicker').css('visibility','visible');
    $('#tags').css('visibility','visible');
    }

  /*
  var more = '<div id="morelink" style="display:none;font-size:8pt;text-align:center">' + morelink() + '</div>';

  $('#datepicker').append(more);
  */

  datepicker = true;
  }


$(document).ready(function(){
		//  console.log("ready");

  var elmcity_id = get_elmcity_id();

  if ( is_eventsonly() || is_mobile() ) 
    {
    if ( gup('tags') == 'no' )
      {
      $('.cat').remove();
      }

    if ( gup('taglist') == 'no' )
      $('.tag_select').remove();
    }

  if ( is_view() && ! is_mobile() )
    try
      {
      var href = $('#subscribe').attr('href');
      href = href.replace('__VIEW__', gup('view'));
      $('#subscribe').attr('href',href);
      $('#subscribe').text('subscribe');
      }
   catch (e)
      {
      }


  if ( gup('timeofday') == 'no' )
    $('.timeofday').remove();

  if ( gup('width') != '' )
    {
    $('#body').css('width', gup('width') + 'px');
    $('div.bl').css('margin','1em 0 0 1em');
    }

  if ( is_theme() )  // invoke it
    { 
//    $('link')[0].href = add_href_arg($('link')[0].href, "theme_name", gup("theme"));
    $('link')[0].href = 'http://elmcity.cloudapp.net/get_css_theme?theme_name=' + gup('theme');
    }

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


  if ( is_mobile() )
    add_fullsite_switcher();
//  else
//    add_mobile_switcher();


//  if ( ! is_mobile() && ! is_eventsonly() )  
//    extend_events(1,false);

  if ( is_eventsonly() )
    return;

  if ( $('#sidebar').css('position') != 'fixed' ) // unframed, no fixed elements
    setTimeout('setup_datepicker()', 200);
  else
    setup_datepicker();                            

  });

function apply_json_css(element,style)
  {
  try 
    {
    var style = decodeURIComponent(gup(style));
    style = style.replace(/'/g,'"');
    $(element).css(JSON.parse(style));
    }
  catch (e)
    {
    console.log(e.description);
    }
  }

function more()
  {
  extend_events(14,true);
  }

function extend_events(extend_days,show_progress)
  {
//  console.log("extend_events");
  $('#morelink').empty();
  
  if ( show_progress )
    $('#morelink').append('<p><img src="http://elmcity.blob.core.windows.net/admin/ajax-loader.gif"></p>');

  var from_day = last_day;
  var from = from_day.toISOString().substring(0,10) + "T00:00";
  var to_day = from_day;
  to_day.addDays(extend_days);
  var to = to_day.toISOString().substring(0,10) + "T00:00";
  var href = location.href;
  href = href.replace(/#.+/,'')
  href = remove_href_arg(href, 'count');
  href = add_href_arg(href, 'from',from);
  href = add_href_arg(href, 'to',to);
  href = add_href_arg(href, 'raw','yes');

  var last_id = find_id_of_last_event();
  var title = get_summary(last_id);
  var dtstart = get_dtstart(last_id);
  var raw_sentinel = title + '__|__' + dtstart;
  href = add_href_arg(href, 'raw_sentinel',raw_sentinel);

//  console.log("extend: " + href);

  $.ajax({
       url: href,
       cache: false,

       complete: function(xhr, status) { 
         var events = xhr.responseText;
         if ( events.length )
           {
           $('.events').append(events);
           prep_day_anchors_and_last_day();
           $('#datepicker').datepicker("option","maxDate", last_day);
           $('#datepicker').datepicker("refresh");  
           $('#morelink').empty();
           $('#morelink').append(morelink());
           show_id('morelink');
           }
         else
           hide_id('morelink');
         }
       });
  }

function setDate(year,month,day)
  {
//  console.log("set_date");
  var date =  $('#datepicker').datepicker('getDate');
  var current_date = $('td > a[class~=ui-state-active]');
  current_date.css('font-weight', 'normal');
  $('#datepicker').datepicker('setDate', new Date(year, month-1, day));
  var td = $('td[class=ui-datepicker-current-day] > a[class~=ui-state-active]');
  var td = $('td > a[class~=ui-state-active]');
  current_date = $('td > a[class~=ui-state-active]');
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
  show = $.inArray( date_str, anchor_names ) == -1 ? false : true;
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
  location.href = '#ym' + year + month;
//  setDate(year, parseInt(month), 1);
  }

function maybeZeroPad(str)
  {
  if ( str.length == 1 ) str = '0' + str;
  return str;
  }


function show_id (id)
  {
  var id = '#' + id;
  var display = $(id).get(0).style.display;
  if ( display == 'none' )
    $(id).show();
  }

function hide_id (id)
  {
  var id = '#' + id;
  var display = $(id).get(0).style.display;
  if ( display != 'none' )
    $(id).hide();
  }



function scrollToElement(id) 
  {
//  $("html, body").animate({
//        scrollTop: $('#' + id).offset().top }, 0
//    );
  window.scrollTo(0, $('#' + id).offset().top);
  }

function show_more(id)
  {
  $('div.' + id).show();
  $('span.' + id).remove();
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

function show_view()
  {
//  var selected = $('#tag_select option:selected').text();
  var selected = $('#tag_select option:selected').val();
  selected = selected.replace(/\s*\((\d+)\)/,'');
  var elmcity_id = get_elmcity_id();
  var href;
  if ( selected == 'all' )
    href = '/' + elmcity_id + '/';
  else
    href = '/' + elmcity_id + '/?view=' + encodeURIComponent(selected);
  if ( gup('test') != '')
    href = add_href_arg(href,'test',gup('test') );
  if ( gup('theme') != '')
    href = add_href_arg(href,'theme',gup('theme') );
  if ( gup('count') != '')
    href = add_href_arg(href,'count',gup('count') );
  location.href = href;
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
var elt = $('#' + id);
elt.find('.menu').remove();
}

function get_add_to_cal_url(id,flavor)
{
var elt = $('#' + id);
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
  $('.menu').remove();
//  console.log('redirecting to ' + service_url);
//  location.href = service_url;
  window.open(service_url, "add to google");
  }
catch (e)
  {
  console.log(e.description);
  }
}

function add_to_hotmail(id)
{
var service_url = get_add_to_cal_url(id, 'hotmail');
$('.menu').remove();
location.href = service_url;
}

function add_to_ical(id)
{
var service_url = get_add_to_cal_url(id, 'ical');
$('.menu').remove();
location.href = service_url;
}

function add_to_facebook(id)
{
var service_url = get_add_to_cal_url(id, 'facebook');
$('.menu').remove();
location.href = service_url;
}


function add_to_cal(id)
{
elt = $('#' + id);
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
quoted_id = '\'' + current_id + '\'';
var x = '<span><a title="hide description" href="javascript:hide_desc(' + quoted_id + ')">[x]</a> </span>';

var s = '<div style="overflow:hidden;text-indent:0" id="' + current_id + '_desc' + '">' + description + ' ' + x + '</div>';

elt = $('#' + current_id);

s = s.replace('<br><br>','<br>');

elt.append(s);
}


function hide_desc(id)
{
quoted_id = '\'' + id + '\'';

$('#' + id + '_desc').remove();
$('#' + id + ' .sd').css('display','inline');
$('#' + id + ' .atc').css('display','inline');
}


function show_desc(id)
{
quoted_id = '\'' + id + '\'';

$('#' + id + ' .sd').css('display','none');
$('#' + id + ' .atc').css('display','none');


var _dtstart = get_dtstart(id);
var _title = get_summary(id);
var elmcity_id = get_elmcity_id();
_active_description = "";
var url = host + elmcity_id + '/description_from_title_and_dtstart?title=' + encodeURIComponent(_title) + '&dtstart=' + _dtstart + '&jsonp=active_description';

current_id = id;

$.getScript(url, function(data, textStatus){});
}

function find_id_of_last_event()
  {
  var events = $('.bl');
  var last = events[events.length-1];
  return last.attributes['id'].value;
  }

function get_summary(id)
  {
  var elt = $('#' + id);
  var summary = $('#' + id + ' .ttl span').text();
  if ( summary == '')
    summary = $('#' + id + ' .ttl a').text();
  return summary;
  }

function get_dtstart(id)
  {
  return $('#' + id + ' .st').attr('content');
  }

$.extend({
    keys:  function(obj){
        var a = [];
        $.each(obj, function(k){ a.push(k) });
        return a;
      }
   });














