import { commands } from "vscode";

export enum BuiltInCommands {
    SetContext = 'setContext'
}

export enum CommandContext {
    Enabled = 'spocr:enabled'
}

export function setCommandContext(key: CommandContext | string, value: any) {
    return commands.executeCommand(BuiltInCommands.SetContext, key, value);
}