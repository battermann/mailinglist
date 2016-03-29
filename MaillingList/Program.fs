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
    
    let private updateDb cs updater =
        let db = DbSchema.GetDataContext(cs)
        updater db
        db.DataContext.SubmitChanges()

    let insert cs list =
        let insert (db:Database) = 
            list
            |> List.map (fun e -> toDto e.Email e.Name)
            |> db.MailingList.InsertAllOnSubmit
        let updater () = updateDb cs insert
        tryF updater DbUpdateFailure

    let delete cs =
        let delete (db:Database) = db.MailingList |> db.MailingList.DeleteAllOnSubmit
        let updater () = updateDb cs delete
        tryF updater DbUpdateFailure

module CsvAccess =
    open FSharp.Data
    open ErrorHandling

    type MailingListData = CsvProvider<"mailinglist.csv">

    let readFromCsvFile (fileName:string) = 
        tryF (fun () ->
            let data = MailingListData.Load(fileName)
            [for row in data.Rows do
                yield { Email = Email row.Email; Name = Name row.Name}]) CsvFileAccessFailure

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
        tryF (fun () ->
            let parser = ArgumentParser.Create<CliArguments>()
            let results = parser.Parse args
            results.GetAllResults()) CliArgumentParsingFailure

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

    let handleError err =
        match err with
        | DbUpdateFailure ex           -> printfn "FAILURE: %s" "An error occurred while accessing the database."
        | CsvFileAccessFailure ex      -> printfn "FAILURE: %s" "An error occurred while accessing the CSV file."
        | CliArgumentParsingFailure ex -> printfn "FAILURE: %s\n%s" "An error occurred while parsing the command line argument(s)." ex.Message
        | ConfigurationFailure ex      -> printfn "FAILURE: %s" "An error occurred while reading from the application's configuration file."

    open System.IO
    open System 

    let log err =
        let makeLogMsg err = 
            match err with
            | DbUpdateFailure ex           -> [sprintf "%s, database update failure" (DateTime.Now.ToString()); sprintf "%A" ex]
            | CsvFileAccessFailure ex      -> [sprintf "%s, CSV file access failure" (DateTime.Now.ToString()); sprintf "%A" ex]
            | CliArgumentParsingFailure ex -> [sprintf "%s, CLI argument parsing failure" (DateTime.Now.ToString()); sprintf "%A" ex]
            | ConfigurationFailure ex      -> [sprintf "%s, configuration failure" (DateTime.Now.ToString()); sprintf "%A" ex]
        try File.AppendAllLines("log.txt", err |> makeLogMsg) with _ -> ()

    [<EntryPoint>]
    let main argv = 
        let result = run argv

        match result with
        | Ok _     -> printfn "SUCCESS"
        | Bad errs -> 
            errs |> List.iter handleError
            errs |> List.iter log
        0