<?php


//the log file to use - change this
$myFile = "pinglog.txt";

// Redirect file to return after logging
$redirect_location = 'http://www.vilett.com/disParity/version.txt';

// filter out one dood's script that runs disParity every 20 minutes 
if ($_GET["id"]=="264003054")
  exit;

# $_GET is superglobal array from URL string
if($_GET){

	foreach ($_GET as $key=>$val){
	
		//rough filtering of input
		$cleaninput[$key] = strip_tags($val);
	
	}

}

// change timezone to PST
date_default_timezone_set("America/Los_Angeles");

//note Datetime
$stringData .= date('Y-m-d h:i:s')." ";

//note requesting IP address
$stringData .= $_SERVER['REMOTE_ADDR']." ";


foreach ($cleaninput as $key=>$val){
	//build the string to log 
	$stringData .= "$key = $val | ";
}
$stringData .= "\n";


//open the file
$fh = fopen($myFile, 'a') or die("can't open file");

//write to file
fwrite($fh, $stringData);

//close the file
fclose($fh);




//load the text file for return - change this 
header('Location:'.$redirect_location);


