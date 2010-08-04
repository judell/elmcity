function setDate(year,month,day)
  {
  $('#datepicker').datepicker('setDate', new Date(year, month-1, day));
  }

function scroll(caller)
  {
  var caller = caller;
  name = find_current_name();
  var match = /d(\d{4,4})(\d{2,2})(\d{2,2})/.exec(name);
  var year = match[1];
  var month = match[2];
  var day = match[3];
  setDate(year,month,day);
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
  $('#datepicker').datepicker(
    {  onSelect: function(dateText, inst) { go(dateText); }  }
   );
  scroll('doc.ready');
  });

function go(dateText)
  {
  var groups = dateText.match(/(\d+)\/(\d+)\/(\d+)/);
  var month = groups[1];
  var day = groups[2];
  var year = groups[3];
  location.href = '#d' + year + month + day;
  setDate(year,month,day);
  }

function show(id)
  {
  var datepicker = '#datepicker';
  var sources = '#sources';
  var contribute = '#contribute';
  var id = '#' + id;
  var display = $(id).get(0).style.display;
  if ( display == 'none' )
    $(id).show();
  var sources_visible= $(sources).get(0).style.display != 'none';
  var contribute_visible= $(contribute).get(0).style.display != 'none';
  if ( sources_visible || contribute_visible )
    $(datepicker).hide();
  else
    $(datepicker).show();
  }

function hide(id)
  {
  var datepicker = '#datepicker';
  var sources = '#sources';
  var contribute = '#contribute';
  var id = '#' + id;
  var display = $(id).get(0).style.display;
  if ( display != 'none' )
    $(id).hide();
  var sources_visible= $(sources).get(0).style.display != 'none';
  var contribute_visible= $(contribute).get(0).style.display != 'none';
  if ( ! ( sources_visible || contribute_visible ) )
    $(datepicker).show();
  }

function toggle(id)
  {
  var display = $('#'+id).get(0).style.display;
  if ( display == 'none' )
    show(id);
  else
    hide(id);
  }


