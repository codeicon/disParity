<?php


//the log file to use
$myFile = "pinglog.txt";

$releaseVer = "0.39";
$betaVer = "0.41";

// filter out one dood's script that runs disParity every 20 minutes 
//if ($_GET["id"]=="264003054")
//  exit;

// ...and another one that runs every hour
//if ($_GET["id"]=="2104775708")
//  exit;

$version = floatval($_GET["ver"]); 
// ...log old command line version pings to different file
if ($version <= 0.22)
  $myFile = "pinglog_old.txt";
  
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
$stringData .= date('Y-m-d H:i:s')." ";

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

$beta = $cleaninput["beta"];

if (($beta == "1") && ($version >= 0.40))
  echo $betaVer;
else
  echo $releaseVer;


