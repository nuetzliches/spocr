'use strict';

import { ExtensionContext, workspace } from 'vscode';
import { setCommandContext, CommandContext } from './constants';

export async function activate(context: ExtensionContext) {
    setCommandContext(CommandContext.Enabled, true);
    
    workspace.findFiles('spocr.json', '**/node_modules/**', 1).then(i => {
        if (!(i && i.length)) {
            return;
        }
        const spocrFileUri = i[0];

        workspace.openTextDocument(spocrFileUri).then((document) => {
            const config = JSON.parse(document.getText()) as ISpocrConfig;
            console.log(config.Version);
            
            setCommandContext(CommandContext.Enabled, true);
        });
    });
}

export function deactivate() { }

export interface ISpocrConfig {
    Version: string;
}