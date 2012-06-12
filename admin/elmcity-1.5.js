function on_load()
  {
  var elmcity_id = get_elmcity_id();

  $.ajax({
    url: 'http://elmcity.cloudapp.net/' + elmcity_id + '/tag_cloud',
    complete: function(xhr, status) {
     var tags_json = JSON.parse(xhr.responseText);
     tags(tags_json);
     }
   });
  }

function get_elmcity_id()
  {
  return $('#elmcity_id').text();
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

function scroll(caller)
  {
  var date_str = find_current_name().replace('d','');
  var parsed = parse_yyyy_mm_dd(date_str)
  setDate(parsed['year'], parsed['month'], parsed['day']);
  }

function find_last_day()
  {
  var last_anchor = anchor_names[anchor_names.length - 1];
  var parsed = parse_yyyy_mm_dd(last_anchor.replace('d',''));
  return new Date(parsed['year'], parsed['month'] - 1, parsed['day']);
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


function find_current_name()
  {
  var before = [];
  var datepicker_top = $("#datepicker")[0].getClientRects()[0].top;
  var datepicker_bottom = $("#datepicker")[0].getClientRects()[0].bottom;
  var datepicker_height = datepicker_bottom - datepicker_top;
  var datepicker_center = datepicker_top + ( datepicker_height / 2 );
  var anchors = $("a[name]");
  for (var i = 0; i < anchors.length; i++)
    {
    var anchor = anchors[i];
    var anchor_top = anchor.getClientRects()[0].top;
    if ( anchor_top < datepicker_center )
      before.push(anchor.name);
    else
      break;
    }
  return before[before.length-1];  
  }

$(window).scroll(function() {
  scroll('window.scroll');
});


//$(window).load(function () {
//  window.scrollTo(0,0);
//});


$(document).ready(function(){

  var anchors = $("a[name]");
  anchor_names = get_anchor_names(anchors);
  var today = new Date();
  var last_day = find_last_day();

  $('#datepicker').datepicker({  
		onSelect: function(dateText, inst) { goDay(dateText); },
		onChangeMonthYear: function(year, month, inst) { goMonth(year, month); },
		minDate: today,
		maxDate: last_day,
		hideIfNoPrevNext: true,
        beforeShowDay: maybeShowDay
    });

  setDate(today.getFullYear(), today.getMonth() + 1, today.getDate());

  });

function setDate(year,month,day)
  {
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
  location.href = '#d' + year + month + day;
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


function show(id)
  {
  var id = '#' + id;
  var display = $(id).get(0).style.display;
  if ( display == 'none' )
    $(id).show();
  }

function hide(id)
  {
  var id = '#' + id;
  var display = $(id).get(0).style.display;
  if ( display != 'none' )
    $(id).hide();
  }


$.extend({
    keys:  function(obj){
        var a = [];
        $.each(obj, function(k){ a.push(k) });
        return a;
    }
});


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


String.prototype.startsWith = function (str){
    return this.indexOf(str) == 0;
};


function tags(tags_json)
  {
  var array = [];
  for (i in tags_json)
    {
    var obj = tags_json[i];
    var key = $.keys(obj)[0];
    array.push(key);
    }

  array.sort(case_insensitive_sort);
  remove(array,"...");
  remove(array,"http:");

  if ( array.length > 0 )
    {
    $('#tags').append('<p>tags</p>');
    $('#tags').append('<select id="tag_select" onchange="show_view()">');

    if ( gup('view') != '' )
      $('#tag_select').append('<option>all</option>');
    else
      $('#tag_select').append('<option selected>all</option>');

    for ( i in array )
      {
      var key = array[i];
      var obj = $.grep(tags_json, function (o) { return $.keys(o)[0] == key;} );
      var count = key == "all" ? '' : ' (' + obj[0][key] + ')</p>';
      var selected = '';
      if ( gup('view') == key )
         selected = ' selected';
      $('#tag_select').append('<option ' + selected + ' value="' + key + '">' + key + count + '</option>');
      }

    $('#tags').append('</select>');
    }

  }

function case_insensitive_sort(a, b) 
  {
  var x = a.toLowerCase();
  var y = b.toLowerCase();
  return ((x < y) ? -1 : ((x > y) ? 1 : 0));
  }

function show_view()
  {
  var selected = $('#tag_select option:selected').text();
  selected = selected.replace(/\s*\((\d+)\)/,'');
  var elmcity_id = get_elmcity_id();
  if ( selected == 'all' )
    location.href = '/' + elmcity_id + '/html';
  else
    location.href = '/' + elmcity_id + '/html?view=' + selected;
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
var summary = elt.find('.ttl').find('a')[0].text; // capture first link text
if ( summary ==  ' 1 ' ) // if it is a coalesced title
  summary = elt.find('.ttl').find('span')[0].innerText; // capture the bare text
var url = elt.find('.ttl').find('a').attr('href');
var description = elt.find('.src').text();
var location = ''; // for now

var elmcity_id = get_elmcity_id();

var service_url = 'http://elmcity.cloudapp.net/add_to_cal?elmcity_id=' + elmcity_id + 
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
var service_url = get_add_to_cal_url(id, 'google');
$('.menu').remove();
location.href = service_url;
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
  '<li><a title="add to Facebook" href="javascript:add_to_facebook(' + quoted_id + ')">add to Facebook</a></li>' +
  '<li><a title="dismiss this menu" href="javascript:dismiss_menu(' + quoted_id + ')">cancel</a></li>' + 
  '</ul>'
  );
}

var current_id;

function active_description(description)
{
quoted_id = '\'' + current_id + '\'';
var x = '<span><a title="hide description" href="javascript:hide_desc(' + quoted_id + ')">[x]</a> </span>';

var s = '<p style="overflow:hidden;text-indent:0" id="' + current_id + '_desc' + '">' + x + description + '</p>'

elt = $('#' + current_id);

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


var _dtstart = $('#' + id + ' .st').attr('content');
var _title = $('#' + id + ' .ttl span').text(); 
if ( _title == '' )
  _title = $('#' + id + ' .ttl a').text();
var elmcity_id = get_elmcity_id();
_active_description = "";
var url = 'http://elmcity.cloudapp.net/' + elmcity_id + '/description_from_title_and_dtstart?title=' + encodeURIComponent(_title) + '&dtstart=' + _dtstart + '&jsonp=active_description';

current_id = id;

$.getScript(url, function(data, textStatus){});
}

