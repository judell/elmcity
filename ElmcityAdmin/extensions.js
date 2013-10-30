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


if(!Array.indexOf){
  Array.prototype.indexOf = function(obj){
   for(var i=0; i<this.length; i++){
    if(this[i]==obj){
     return i;
    }
   }
   return -1;
  }
}