namespace Fluence.Core.Models.Theming;

public static class DefaultTextMateTheme
{
    public const string Json = """
    {
      "name": "fluence-default-dark",
      "settings": [
        {
          "settings": {
            "background": "#202225",
            "foreground": "#CBD1D8",
            "selection": "#3A4652"
          }
        },
        {
          "name": "Comment",
          "scope": "comment, punctuation.definition.comment",
          "settings": {
            "foreground": "#6A9955"
          }
        },
        {
          "name": "Documentation Comment",
          "scope": "comment.documentation",
          "settings": {
            "foreground": "#608B4E"
          }
        },
        {
          "name": "Keyword",
          "scope": "keyword, keyword.control, storage, storage.type, storage.modifier, keyword.operator.expression, keyword.other",
          "settings": {
            "foreground": "#569CD6"
          }
        },
        {
          "name": "Control Flow / Special Keywords",
          "scope": "keyword.control, keyword.other.using, keyword.other.operator, entity.name.operator, keyword.operator.expression.await",
          "settings": {
            "foreground": "#C586C0"
          }
        },
        {
          "name": "Operator and Punctuation",
          "scope": "keyword.operator, punctuation",
          "settings": {
            "foreground": "#D4D4D4"
          }
        },
        {
          "name": "String",
          "scope": "string, entity.name.operator.custom-literal.string, string.value, punctuation.definition.string",
          "settings": {
            "foreground": "#CE9178"
          }
        },
        {
          "name": "Number",
          "scope": "constant.numeric, variable.other.enummember",
          "settings": {
            "foreground": "#B5CEA8"
          }
        },
        {
          "name": "Constant Language",
          "scope": "constant.language, variable.language",
          "settings": {
            "foreground": "#569CD6"
          }
        },
        {
          "name": "Function",
          "scope": "entity.name.function, support.function, entity.name.operator.custom-literal",
          "settings": {
            "foreground": "#DCDCAA"
          }
        },
        {
          "name": "Type",
          "scope": "meta.return-type, support.class, support.type, entity.name.type, entity.name.class, entity.other.inherited-class, storage.type.cs, storage.type.generic.cs",
          "settings": {
            "foreground": "#4EC9B0"
          }
        },
        {
          "name": "Interface and Enum",
          "scope": "entity.name.type.interface, entity.name.type.enum, entity.name.type.type-parameter",
          "settings": {
            "foreground": "#B8D7A3"
          }
        },
        {
          "name": "Struct",
          "scope": "entity.name.type.struct",
          "settings": {
            "foreground": "#86C691"
          }
        },
        {
          "name": "Variable and Parameter",
          "scope": "variable, meta.definition.variable.name, support.variable, entity.name.variable, meta.object-literal.key, entity.other.attribute-name",
          "settings": {
            "foreground": "#9CDCFE"
          }
        },
        {
          "name": "Member",
          "scope": "entity.name.variable.field, variable.other.property, entity.name.namespace, entity.name.type.namespace",
          "settings": {
            "foreground": "#D4D4D4"
          }
        },
        {
          "name": "XML/HTML Tag Name",
          "scope": "entity.name.tag",
          "settings": {
            "foreground": "#569CD6"
          }
        },
        {
          "name": "XML/HTML Attribute Name",
          "scope": "entity.other.attribute-name",
          "settings": {
            "foreground": "#9CDCFE"
          }
        },
        {
          "name": "XML/HTML Attribute Value",
          "scope": "string.quoted.double.xml, string.quoted.single.xml, string.quoted.double.html, string.quoted.single.html",
          "settings": {
            "foreground": "#CE9178"
          }
        },
        {
          "name": "Invalid",
          "scope": "invalid",
          "settings": {
            "foreground": "#F44747"
          }
        }
      ]
    }
    """;
}
