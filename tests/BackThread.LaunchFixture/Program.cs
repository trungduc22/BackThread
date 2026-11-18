if(args.Length!=2)return 2;
await File.WriteAllTextAsync(args[0]+".pending",args[1]);
File.Move(args[0]+".pending",args[0]);
return 0;
