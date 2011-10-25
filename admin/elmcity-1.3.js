function on_load()
  {

  var id = /\/services\/([^/]+)\//.exec(window.location.href)[1];

  $.ajax({
    url: 'http://elmcity.cloudapp.net/services/' + id + '/tags_json',
    complete: function(xhr, status) {
     var tags_json = JSON.parse(xhr.responseText);
     tags(tags_json);
     }
   });

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

function find_text_by_selector(selector, search_text)
  {
  if ( search_text == "" ) return;
  var foundin = $(selector);
  for (var i = 0; i < foundin.length; i++)
    {
    var target_text = foundin[i].innerHTML.toLowerCase();
    var found = ( target_text.indexOf( search_text) );
    if (found != -1) 
       {
        return i;
       }
    }
  return -1;
  }

function find_text()
  {
  var search_text = $('#findbox').val().toLowerCase();
  var title_selector = 'span.eventTitle';
  var source_selector = 'span.eventSource';
  var selectors = [title_selector, source_selector];
  var results = {};

  for ( var i= 0; i < selectors.length; i++ )
    {
    var selector = selectors[i];
    results[selector] = find_text_by_selector(selector, search_text);
    }
  
  for ( var i= 0; i < selectors.length; i++ )
    {
    var selector = selectors[i];
    if ( results[selector] != -1 )
      {
      var blurbs = $('div[class=eventBlurb]');
      var blurb = blurbs[results[selector]];
      var dates = $(blurb).prevAll('a[name^=d]');
      var date_str = $(dates[0]).attr('name');
      var parsed = parse_yyyy_mm_dd(date_str);
      setDate(parsed['year'], parsed['month'], parsed['day']);
      $(blurb).before("<a name='found_text'/>");
      $(blurb).css('text-decoration', 'underline');
      location.href = '#found_text';
      $('a[name=found_text]').remove();
      return;
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

//$('#findform').submit(function(event) {
//    event.preventDefault();
//})


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

  $('#findbox').keypress(function(event){
    if(event.keyCode == 13){
    $("#findsubmit").click();
    }
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
      $('#tag_select').append('<option ' + selected + ' " value="' + key + '">' + key + count + '</option>');
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
  var id = /\/services\/([^/]+)\//.exec(window.location.href)[1];
  if ( selected == 'all' )
    location.href = '/services/' + id + '/html';
  else
    location.href = '/services/' + id + '/html?view=' + selected;
  }

