import * as monaco from 'monaco-editor';

import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import JsonWorker from 'monaco-editor/language/json/json.worker?worker';
import CssWorker from 'monaco-editor/language/css/css.worker?worker';
import HtmlWorker from 'monaco-editor/language/html/html.worker?worker';
import TsWorker from 'monaco-editor/language/typescript/ts.worker?worker';

self.MonacoEnvironment = {
    getWorker(_, label) {
        switch (label) {
            case 'json':
                return new JsonWorker();

            case 'css':
            case 'scss':
            case 'less':
                return new CssWorker();

            case 'html':
            case 'handlebars':
            case 'razor':
                return new HtmlWorker();

            case 'typescript':
            case 'javascript':
                return new TsWorker();

            default:
                return new EditorWorker();
        }
    }
};

window.monaco = monaco;

window.dispatchEvent(
    new CustomEvent('labelflow-monaco-ready')
);

export { monaco };
