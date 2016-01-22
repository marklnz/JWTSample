var gulp = require("gulp");

var exec = require('child_process').exec;

gulp.task('serve-web', function(cb) {
    exec('http-server', function (err, stdout, stderr) {
    console.log(stdout);
    console.log(stderr);
    cb(err);
  });        
});
