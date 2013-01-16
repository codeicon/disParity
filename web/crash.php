<?php

date_default_timezone_set("America/Los_Angeles");

// Where the file is going to be placed   
$target_path = "logs/" . $_POST["id"] . "-" .date('Y-m-d-His') . ".crash.txt";

//open the file
$fh = fopen($target_path, 'w') or die("can't open file");

//write to file
fwrite($fh, $_POST["crash"]);

//close the file
fclose($fh);

