type Email = Email of string

type Name = Name of string

type MailingListEntry = {
    Email:Email
    Name:Name }

module DataAccess =
    open Microsoft.FSharp.Data.TypeProviders

    [<Literal>]
    let ConnectionString = @"Data Source=(LocalDb)\V11.0;Initial Catalog=MailingListDb;Integrated Security=True"
    type DbSchema = SqlDataConnection<ConnectionString>

    let private toDto (Email email) (Name name) =
        DbSchema.ServiceTypes.MailingList(Email = email, Name = name)

    let insert cs list =
        let db = DbSchema.GetDataContext(cs)
        list
        |> List.map (fun e -> toDto e.Email e.Name)
        |> db.MailingList.InsertAllOnSubmit
        |> db.DataContext.SubmitChanges

    let delete cs =
        let db = DbSchema.GetDataContext(cs)
        db.MailingList
        |> db.MailingList.DeleteAllOnSubmit
        |> db.DataContext.SubmitChanges

module CsvAccess =
    open FSharp.Data

    type MailingListData = CsvProvider<"mailinglist.csv">

    let readFromCsvFile (fileName:string) = 
        let data = MailingListData.Load(fileName)
        [for row in data.Rows do
            yield { Email = Email row.Email; Name = Name row.Name}]

module Arguments =
    open Argu

    type CliArguments =
        | Import of fileName:string
        | Delete
    with 
        interface IArgParserTemplate with
            member s.Usage = match s with Import _ -> "import csv data" | Delete -> "delete all entries"

    let getCmds args =
        let parser = ArgumentParser.Create<CliArguments>()
        let results = parser.Parse args
        results.GetAllResults()

module Program =
    open FSharp.Configuration
    open Arguments
    open CsvAccess

    // inject connection srtring
    type Settings = AppSettings<"app.config">
    let private import list = DataAccess.insert Settings.ConnectionStrings.MyMailingListDb list
    let private delete() = DataAccess.delete Settings.ConnectionStrings.MyMailingListDb

    let private handle cmd =
        match cmd with
        | Import fileName -> readFromCsvFile fileName |> import
        | Delete          -> delete()

    let private run args =
        getCmds args
        |> List.iter handle

    [<EntryPoint>]
    let main argv = 
        run argv
        0