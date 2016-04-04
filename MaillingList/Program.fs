type Email = Email of string

type Name = Name of string

type MailingListEntry = {
    Email:Email
    Name:Name }

module ErrorHandling =
    open System
    open Chessie.ErrorHandling

    type DomainMessage = 
        | DbUpdateFailure of Exception
        | CsvFileAccessFailure of Exception
        | CliArgumentParsingFailure of Exception
        | ConfigurationFailure of Exception

    let tryF f msg =
        try f() |> ok with ex -> fail (msg ex)

module DataAccess =
    open Microsoft.FSharp.Data.TypeProviders
    open ErrorHandling

    [<Literal>]
    let ConnectionString = @"Data Source=(LocalDb)\V11.0;Initial Catalog=MailingListDb;Integrated Security=True"
    type DbSchema = SqlDataConnection<ConnectionString>
    type Database = DbSchema.ServiceTypes.SimpleDataContextTypes.MailingListDb

    let private toDto (Email email) (Name name) =
        DbSchema.ServiceTypes.MailingList(Email = email, Name = name)

    let insert cs list =
        let insert () =
            let db = DbSchema.GetDataContext(cs)
            do list
               |> List.map (fun e -> toDto e.Email e.Name)
               |> db.MailingList.InsertAllOnSubmit
               |> db.DataContext.SubmitChanges
        tryF insert DbUpdateFailure

    let delete cs =
        let delete () = 
            let db = DbSchema.GetDataContext(cs)
            do db.MailingList 
               |> db.MailingList.DeleteAllOnSubmit
               |> db.DataContext.SubmitChanges
        tryF delete DbUpdateFailure

module CsvAccess =
    open FSharp.Data
    open ErrorHandling

    type MailingListData = CsvProvider<"mailinglist.csv">

    let readFromCsvFile (fileName:string) = 
        let read () =
            let data = MailingListData.Load(fileName)
            [for row in data.Rows do
                yield { Email = Email row.Email; Name = Name row.Name}]
        tryF read CsvFileAccessFailure

module Arguments =
    open Argu
    open ErrorHandling

    type CliArguments =
        | Import of fileName:string
        | Delete
    with 
        interface IArgParserTemplate with
            member s.Usage = match s with Import _ -> "import csv data" | Delete -> "delete all entries"

    let getCmds args =
        let parse () =
            let parser = ArgumentParser.Create<CliArguments>()
            let results = parser.Parse args
            results.GetAllResults()
        tryF parse CliArgumentParsingFailure

module Program =
    open FSharp.Configuration
    open Arguments
    open CsvAccess
    open ErrorHandling
    open Chessie.ErrorHandling

    // inject connection srtring
    type Settings = AppSettings<"app.config">

    let private csResult = tryF (fun () -> Settings.ConnectionStrings.MyMailingListDb) ConfigurationFailure

    let private import list = csResult >>= fun cs -> DataAccess.insert cs list
    let private delete() = csResult >>= DataAccess.delete

    let private handle cmd =
        match cmd with
        | Import fileName -> readFromCsvFile fileName >>= import
        | Delete          -> delete()

    let private run args =
        getCmds args >>= (Seq.map handle >> Trial.collect)

    let private handleError err =
        match err with
        | DbUpdateFailure ex           -> do printfn "FAILURE: %s" "An error occurred while accessing the database."
        | CsvFileAccessFailure ex      -> do printfn "FAILURE: %s" "An error occurred while accessing the CSV file."
        | CliArgumentParsingFailure ex -> do printfn "FAILURE: %s\n%s" "An error occurred while parsing the command line argument(s)." ex.Message
        | ConfigurationFailure ex      -> do printfn "FAILURE: %s" "An error occurred while reading from the application's configuration file."

    let private makeLogMsg datetime err = 
        match err with
        | DbUpdateFailure ex           -> sprintf "[%A], [ERROR], [DbUpdateFailure]\r\n%A" datetime  ex
        | CsvFileAccessFailure ex      -> sprintf "[%A], [ERROR], [CsvFileAccessFailure]\r\n%A" datetime ex
        | CliArgumentParsingFailure ex -> sprintf "[%A], [ERROR], [CliArgumentParsingFailure]\r\n%A" datetime ex
        | ConfigurationFailure ex      -> sprintf "[%A], [ERROR], [ConfigurationFailure]\r\n%A" datetime ex

    let private fileLogger fileName msg =
        do try System.IO.File.AppendAllText(fileName, sprintf "%s\r\n" msg) with _ -> ()

    [<EntryPoint>]
    let main argv = 
        let result = run argv

        match result with
        | Ok _     -> do printfn "SUCCESS"
        | Bad errs -> do errs |> List.iter handleError
                      do errs |> List.iter (makeLogMsg System.DateTime.Now >> fileLogger "log.txt")
        0