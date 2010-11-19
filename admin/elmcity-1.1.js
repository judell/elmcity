function setDate(year,month,day)
  {
  $('#datepicker').datepicker('setDate', new Date(year, month-1, day));
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
  var caller = caller;
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

function get_anchor_names()
  {
  var anchors = $("a[name]");
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


$(document).ready(function(){
  anchor_names = get_anchor_names();
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
  scroll('doc.ready');
  setDate(today);
  find_last_day();  
  });

function maybeShowDay(date)
  {
  var year = date.getFullYear();
  var month = date.getMonth() + 1;
  var day = date.getDate();
  month = maybeZeroPad(month.toString());
  day = maybeZeroPad(day.toString());
  var date_str = "d" + year + month + day;
  var show = $.inArray( date_str, anchor_names ) == -1 ? false : true;
  var style = show == false ? "ui-datepicker-unselectable ui-state-disabled" : "";
  return [show, style]
  }

function goDay(date_str)
  {
  var parsed = parse_mm_dd_yyyy(date_str)
  var year = parsed['year'];
  var month = parsed['month'];
  var day = parsed['day'];
  location.href = '#d' + year + month + day;
  setDate(year, month, day);
  }

function goMonth(year, month)
  {
  month = maybeZeroPad(month.toString());
  location.href = '#ym' + year + month;
  setDate(year, parseInt(month), 1);
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


